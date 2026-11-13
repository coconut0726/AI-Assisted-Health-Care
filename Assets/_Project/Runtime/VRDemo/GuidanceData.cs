using System;
using System.Collections.Generic;
using UnityEngine;

namespace AHC.VR
{
    // Demo content for the headset, loaded from Assets/_Project/Data/BurnDressingDemo.json.
    // Steps/actions mirror the Decompose tool's output; overlays add where each piece of
    // guidance is drawn, in metres relative to the Workspace origin (the burn on the table).

    [Serializable]
    public class GuidanceData
    {
        public string procedureTitle;
        public string nurseName;
        public string patientName;
        public string updateNotice;
        public string estimate;
        public string redFlags;
        public TrackedObject[] objects;
        public Supply[] supplies;
        public StepData[] steps;
        public string[] nurseTranscript;

        public static GuidanceData FromJson(string json) => JsonUtility.FromJson<GuidanceData>(json);

        public TrackedObject FindObject(string id)
        {
            foreach (var o in objects)
                if (o.id == id) return o;
            return null;
        }

        /// <summary>Every action in order, as (step index, action index).</summary>
        public List<(int step, int action)> Flatten()
        {
            var list = new List<(int, int)>();
            for (int s = 0; s < steps.Length; s++)
                for (int a = 0; a < steps[s].actions.Length; a++)
                    list.Add((s, a));
            return list;
        }
    }

    [Serializable]
    public class TrackedObject
    {
        public string id;
        public string label;
        public Vector3 position;
        public Vector3 size;
    }

    [Serializable]
    public class Supply
    {
        public string label;
        public bool found;
    }

    [Serializable]
    public class StepData
    {
        public string title;
        public string expectedState;
        public string sourceSpan;
        public bool updated;
        public ActionData[] actions;
    }

    [Serializable]
    public class ActionData
    {
        public string label;
        public string voice;
        public OverlayData[] overlays;
    }

    [Serializable]
    public class OverlayData
    {
        /// <summary>box | next | widget | chip | warn | gesture | arc</summary>
        public string type;
        public string target;
        public string text;
        public string pill;
        public string title;
        public string note;
    }
}
