using System.Linq;
using GHPC.Weapons;
using UnityEngine;

namespace LeclercSXXI.Common
{
	internal static class KordBelt
	{
		[DefaultExecutionOrder(20)]
		internal class Belt : MonoBehaviour
		{
			// Half is a guess, the source only gives the order of the motions.
			const float FeedPart = 0.5f;
			internal Transform Cupola, Cradle;
			internal Vector3 FeedOnCradle, FeedDirOnCradle, FeedUpOnCradle;
			internal Vector3 Mouth, MouthDir;
			internal float Pitch;
			internal (Transform t, float s)[] Rounds;
			internal float[] RestS;
			internal Vector3[] CartPos;
			internal Quaternion[] CartRot;

			(Vector3 p, Quaternion r) Cartridge(float s)
			{
				int n = RestS.Length;
				if (s <= RestS[0]) return (CartPos[0], CartRot[0]);
				if (s >= RestS[n - 1]) return (CartPos[n - 1], CartRot[n - 1]);
				int i = 0;
				while (i < n - 2 && RestS[i + 1] < s) i++;
				float u = (s - RestS[i]) / Mathf.Max(1e-6f, RestS[i + 1] - RestS[i]);
				return (Vector3.Lerp(CartPos[i], CartPos[i + 1], u), Quaternion.Slerp(CartRot[i], CartRot[i + 1], u));
			}

			internal Mesh TypicalMesh;
			internal float FeedPitch;
			float Drop => Mathf.Max(0.02f, FeedOnCradle.y);
			Renderer[] _renderers;
			WeaponSystem _weapon;
			AmmoFeed _feed;
			float _cycle, _fed, _target, _fedSince;
			int _head;

			void OnDestroy() { if (_weapon != null) _weapon.Fired -= OnFired; }

			void OnFired(AmmoType ammo, LiveRound round) { _cycle = Cycle(_weapon, _cycle); _target += FeedPitch; }

			internal static float Cycle(WeaponSystem w, float fallback)
			{
				float c = w != null && RemoteMg.WeaponCycle != null ? (float)RemoteMg.WeaponCycle.GetValue(w) : 0f;
				return c > 0f ? c : fallback;
			}

			void LateUpdate() => Tick();

			void Tick()
			{
				if (_target > _fed)
				{
					float was = _fed;
					_fed = Mathf.MoveTowards(_fed, _target, FeedPitch / Mathf.Max(0.01f, FeedPart * _cycle) * Time.deltaTime);
					_fedSince = Mathf.Min(1f, _fedSince + _fed - was);
					while (_fed - _head * FeedPitch >= FeedPitch - 1e-5f)
					{
						var mf = Rounds[_head % Rounds.Length].t.GetComponent<MeshFilter>();
						if (TypicalMesh != null && mf.sharedMesh != TypicalMesh) mf.sharedMesh = TypicalMesh;
						_head++;
					}
					if (_head >= Rounds.Length) { _head -= Rounds.Length; _fed -= Rounds.Length * FeedPitch; _target -= Rounds.Length * FeedPitch; }
				}
				Place();
			}

			internal void Place()
			{
				if (Cupola == null || Cradle == null || Rounds == null) return;
				var feed = Cupola.InverseTransformPoint(Cradle.TransformPoint(FeedOnCradle));
				var feedDir = Cupola.InverseTransformDirection(Cradle.TransformDirection(FeedDirOnCradle));
				var feedUp = Cupola.InverseTransformDirection(Cradle.TransformDirection(FeedUpOnCradle));
				if (_renderers == null) _renderers = Rounds.Select(r => r.t.GetComponent<Renderer>()).ToArray();
				int left = _feed != null ? _feed.CurrentClipRemainingCount - (_feed.AmmoTypeInBreech != null ? 1 : 0) : int.MaxValue;
				if (_placed && _fed == _lastFed && _head == _lastHead && left == _lastLeft && feed == _lastFeed && feedDir == _lastFeedDir && feedUp == _lastFeedUp) return;
				if (_placed && _target <= _fed && !Seen()) return;
				_placed = true; _lastFed = _fed; _lastHead = _head; _lastLeft = left; _lastFeed = feed; _lastFeedDir = feedDir; _lastFeedUp = feedUp;
				var c = _curve ?? (_curve = new BeltCurve(feed, feedDir, feedUp, Mouth, MouthDir, Vector3.up));
				c.Set(feed, feedDir, feedUp, Mouth, MouthDir, Vector3.up);
				int nAll = Rounds.Length;
				float frac = _fed - _head * FeedPitch;
				float fade = Mathf.Clamp01(1f - _fedSince / (2f * FeedPitch));
				for (int i = 0; i < nAll; i++)
				{
					var (t, s0) = Rounds[i];
					int q = ((i - _head) % nAll + nAll) % nAll;
					float s = q * FeedPitch - frac + (s0 - i * FeedPitch) * fade;
					// Renderer, not the object: the burn effect and heat source collect active renderers once, at Start.
					bool show = s <= c.Length + 0.3f * Pitch && s > -0.95f * FeedPitch && q < left;
					if (_renderers[i].enabled != show) _renderers[i].enabled = show;
					if (!show) continue;
					var f = c.Frame(Mathf.Max(0f, s));
					var (cp, cr) = Cartridge(Mathf.Max(0f, s));
					var p = f.MultiplyPoint3x4(cp);
					if (s < 0f) p -= feedUp.normalized * (Mathf.Clamp01(-s / FeedPitch) * Drop);
					t.localPosition = p;
					t.localRotation = f.rotation * cr;
				}
			}

			BeltCurve _curve;
			bool _placed;
			float _lastFed;
			int _lastHead, _lastLeft;
			Vector3 _lastFeed, _lastFeedDir, _lastFeedUp;

			bool Seen()
			{
				foreach (var r in _renderers) if (r != null && r.enabled && r.isVisible) return true;
				return false;
			}
		}
	}
}
