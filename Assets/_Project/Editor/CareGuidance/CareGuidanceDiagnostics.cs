using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HomeCare.Guidance.EditorTools
{
    public static class CareGuidanceDiagnostics
    {
        public static string Runtime()
        {
            var sb = new StringBuilder();
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                sb.Append("EventSystem ").Append(es.name).Append(" [")
                  .Append(string.Join(",", es.GetComponents<BaseInputModule>().Select(m => m.GetType().Name + (m.enabled ? "" : "(off)"))))
                  .Append("]\n");
            sb.Append("current=").Append(EventSystem.current != null ? EventSystem.current.name : "null").Append('\n');
            sb.Append("cursor=").Append(Cursor.lockState).Append('\n');
            var subs = new System.Collections.Generic.List<UnityEngine.XR.Hands.XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            sb.Append("handSubsystems=").Append(subs.Count).Append(" running=").Append(subs.Count(s => s.running)).Append('\n');
            var ht = HandTracking.Instance;
            if (ht != null)
                foreach (var h in ht.Hands)
                    sb.Append(h.Side).Append(" tracked=").Append(h.IsTracked).Append(" sim=").Append(h.IsSimulated)
                      .Append(" pinch=").Append(h.IsPinching).Append(" tip=").Append(h.IndexTip).Append('\n');
            else sb.Append("HandTracking.Instance=null\n");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.parent == null) sb.Append("root: ").Append(t.name).Append(" (").Append(t.gameObject.scene.name).Append(")\n");
            return sb.ToString();
        }
    }
}
