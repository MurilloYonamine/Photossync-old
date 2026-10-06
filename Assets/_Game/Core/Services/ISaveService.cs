// Autor: Murillo Gomes Yonamine
// Data: 05/05/2026

using System;
using System.Collections.Generic;

namespace FifthSemester.Core.Services {
    public interface ISaveService {
        void SaveToSlot(string slotId, SaveData data);
        SaveData LoadFromSlot(string slotId);
        void DeleteSlot(string slotId);
        bool SlotExists(string slotId);
        string[] ListSlots();
        void SaveCheckpoint(string checkpointId, SaveData data);

        event Action<string> OnSaveCompleted;
    }

    [System.Serializable]
    public class Vector3Data {
        public float x;
        public float y;
        public float z;

        public Vector3Data() { }
        public Vector3Data(UnityEngine.Vector3 v) { x = v.x; y = v.y; z = v.z; }
        public UnityEngine.Vector3 ToVector3() => new(x, y, z);
    }

    [System.Serializable]
    public class QuaternionData {
        public float x;
        public float y;
        public float z;
        public float w;

        public QuaternionData() { }
        public QuaternionData(UnityEngine.Quaternion q) { x = q.x; y = q.y; z = q.z; w = q.w; }
        public UnityEngine.Quaternion ToQuaternion() => new(x, y, z, w);
    }

    [System.Serializable]
    public class MissionProgressEntry {
        public string MissionId;
        public string Progress;
    }

    [System.Serializable]
    public class SaveData {
        public string SceneName = "Game";
        public int CurrentMissionIndex;
        public Dictionary<string, string> MissionProgress = new();
        public List<MissionProgressEntry> MissionProgressEntries = new();
        public string LastCheckpointId = "default";
        public int SaveVersion = 1;
        public long Timestamp;
        
        public string ScreenshotBase64 = "";
        
        public Vector3Data PlayerPosition = new();
        public QuaternionData PlayerRotation = new();
        public bool HasPlayerPosition;
        
        // Camera State
        public Vector3Data CameraTargetPosition = new();
        public QuaternionData CameraTargetRotation = new();

        // Inventory
        public List<string> InventoryItemIds = new();

        public SaveData() {
            Timestamp = System.DateTime.UtcNow.Ticks / 10000000;
        }
    }
}
