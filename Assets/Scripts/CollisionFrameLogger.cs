using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes a frame-by-frame diagnostic log for player and WorldObject state.
/// Intended for debugging collision/carry issues.
/// </summary>
[DefaultExecutionOrder(10000)]
public class CollisionFrameLogger : MonoBehaviour
{
    [Header("Logging")]
    [SerializeField] private bool enableLogging = true;
    [SerializeField] private string logFileName = "collision_frame_log.txt";
    [SerializeField] private bool overwriteOnPlay = true;
    [SerializeField] private int sampleEveryNFrames = 1;
    [SerializeField] private int flushEveryNFrames = 30;
    [Tooltip("If true, write log next to Assets folder (project root). If false, write to Application.persistentDataPath.")]
    [SerializeField] private bool writeToProjectRoot = true;

    [Header("Scope")]
    [Tooltip("If true, include inactive scene objects while collecting tracked objects.")]
    [SerializeField] private bool includeInactiveWorldObjects = false;

    private Transform _player;
    private Collider[] _playerColliders = Array.Empty<Collider>();

    private string _logPath;
    private StreamWriter _writer;
    private int _loggedFrameCount;
    private bool _initialized;

    class TrackedObject
    {
        public Transform transform;
        public string name;
        public int id;
        public Collider[] colliders = Array.Empty<Collider>();
        public bool isCarried;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureLoggerExists()
    {
        if (FindObjectOfType<CollisionFrameLogger>() != null)
            return;

        GameObject go = new GameObject("CollisionFrameLogger");
        DontDestroyOnLoad(go);
        go.AddComponent<CollisionFrameLogger>();
    }

    void Awake()
    {
        InitializeWriter();
    }

    void LateUpdate()
    {
        if (!enableLogging || _writer == null)
            return;

        int sampleStep = Mathf.Max(1, sampleEveryNFrames);
        if ((Time.frameCount % sampleStep) != 0)
            return;

        if (!ResolvePlayer())
            return;

        WriteFrameLog();
    }

    void OnApplicationQuit()
    {
        CloseWriter();
    }

    void OnDestroy()
    {
        CloseWriter();
    }

    void InitializeWriter()
    {
        if (_initialized)
            return;

        if (string.IsNullOrWhiteSpace(logFileName))
            logFileName = "collision_frame_log.txt";

        string basePath = Application.persistentDataPath;
        if (writeToProjectRoot)
            basePath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        _logPath = Path.Combine(basePath, logFileName);

        FileMode mode = overwriteOnPlay ? FileMode.Create : FileMode.Append;
        _writer = new StreamWriter(new FileStream(_logPath, mode, FileAccess.Write, FileShare.Read), Encoding.UTF8);
        _writer.AutoFlush = false;

        if (mode == FileMode.Create)
        {
            _writer.WriteLine("# Collision Frame Logger");
            _writer.WriteLine("# fields: FRAME | PLAYER | OBJECT | PAIR_COLLISION");
        }
        else
        {
            _writer.WriteLine();
            _writer.WriteLine("# --- New Play Session ---");
        }

        _writer.Flush();
        _initialized = true;

        Debug.Log($"[CollisionFrameLogger] Logging to: {_logPath}");
    }

    void CloseWriter()
    {
        if (_writer == null)
            return;

        _writer.Flush();
        _writer.Close();
        _writer = null;
    }

    bool ResolvePlayer()
    {
        if (_player != null && _player.gameObject.activeInHierarchy)
        {
            _playerColliders = _player.GetComponentsInChildren<Collider>();
            return true;
        }

        PlayerController pc = FindObjectOfType<PlayerController>();
        if (pc != null)
        {
            _player = pc.transform;
            _playerColliders = _player.GetComponentsInChildren<Collider>();
            return true;
        }

        CharacterController cc = FindObjectOfType<CharacterController>();
        if (cc != null)
        {
            _player = cc.transform;
            _playerColliders = _player.GetComponentsInChildren<Collider>();
            return true;
        }

        GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null)
        {
            _player = taggedPlayer.transform;
            _playerColliders = _player.GetComponentsInChildren<Collider>();
            return true;
        }

        return false;
    }

    void WriteFrameLog()
    {
        TrackedObject[] objects = CollectTrackedObjects();

        StringBuilder sb = new StringBuilder(32 * 1024);

        Vector3 playerPos = _player.position;
        Vector3 playerRot = NormalizeEuler(_player.rotation.eulerAngles);

        sb.Append("FRAME\t")
          .Append(Time.frameCount)
          .Append("\tTIME\t")
          .Append(Time.time.ToString("F4"))
          .Append('\n');

        sb.Append("PLAYER\tPOS\t")
          .Append(FormatVec(playerPos))
          .Append("\tROT\t")
          .Append(FormatVec(playerRot))
          .Append('\n');

        for (int i = 0; i < objects.Length; i++)
        {
            TrackedObject obj = objects[i];
            Transform t = obj.transform;

            Vector3 worldPos = t.position;
            Vector3 worldRot = NormalizeEuler(t.rotation.eulerAngles);
            float distanceToPlayer = Vector3.Distance(worldPos, playerPos);
            Vector3 relPos = _player.InverseTransformPoint(worldPos);
            Quaternion relRotQ = Quaternion.Inverse(_player.rotation) * t.rotation;
            Vector3 relRot = NormalizeEuler(relRotQ.eulerAngles);
            string playerCollision = EvaluateCollisionState(obj.colliders, _playerColliders);

            sb.Append("OBJECT\tINDEX\t")
              .Append(i)
              .Append("\tNAME\t")
              .Append(obj.name)
              .Append("\tID\t")
              .Append(obj.id)
              .Append("\tPOS\t")
              .Append(FormatVec(worldPos))
              .Append("\tROT\t")
              .Append(FormatVec(worldRot))
              .Append("\tDIST_PLAYER\t")
              .Append(distanceToPlayer.ToString("F4"))
              .Append("\tREL_POS\t")
              .Append(FormatVec(relPos))
              .Append("\tREL_ROT\t")
              .Append(FormatVec(relRot))
              .Append("\tIS_CARRIED\t")
              .Append(obj.isCarried ? "1" : "0")
              .Append("\tCOLLISION_WITH_PLAYER\t")
              .Append(playerCollision)
              .Append('\n');
        }

        for (int i = 0; i < objects.Length; i++)
        {
            for (int j = i + 1; j < objects.Length; j++)
            {
                string state = EvaluateCollisionState(objects[i].colliders, objects[j].colliders);

                sb.Append("PAIR_COLLISION\tA_INDEX\t")
                  .Append(i)
                  .Append("\tA_NAME\t")
                  .Append(objects[i].name)
                  .Append("\tA_ID\t")
                                    .Append(objects[i].id)
                  .Append("\tB_INDEX\t")
                  .Append(j)
                  .Append("\tB_NAME\t")
                  .Append(objects[j].name)
                  .Append("\tB_ID\t")
                                    .Append(objects[j].id)
                  .Append("\tSTATE\t")
                  .Append(state)
                  .Append('\n');
            }
        }

        _writer.Write(sb.ToString());
        _loggedFrameCount++;

        int flushStep = Mathf.Max(1, flushEveryNFrames);
        if ((_loggedFrameCount % flushStep) == 0)
            _writer.Flush();
    }

    TrackedObject[] CollectTrackedObjects()
    {
        Dictionary<Transform, List<Collider>> colliderMap = new Dictionary<Transform, List<Collider>>();
        Dictionary<Transform, bool> carryStateMap = new Dictionary<Transform, bool>();

        Collider[] allColliders = includeInactiveWorldObjects
            ? FindObjectsOfType<Collider>(true)
            : FindObjectsOfType<Collider>();

        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider c = allColliders[i];
            if (c == null) continue;

            Transform owner = c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform;
            if (owner == null) continue;

            if (!colliderMap.TryGetValue(owner, out List<Collider> list))
            {
                list = new List<Collider>();
                colliderMap.Add(owner, list);
            }

            list.Add(c);
        }

        WorldObject[] worldObjects = includeInactiveWorldObjects
            ? FindObjectsOfType<WorldObject>(true)
            : FindObjectsOfType<WorldObject>();

        for (int i = 0; i < worldObjects.Length; i++)
        {
            WorldObject wo = worldObjects[i];
            if (wo == null) continue;

            Transform owner = wo.transform;
            if (owner == null) continue;

            if (!colliderMap.ContainsKey(owner))
                colliderMap.Add(owner, new List<Collider>(wo.GetComponentsInChildren<Collider>()));

            carryStateMap[owner] = wo.IsCarried;
        }

        List<TrackedObject> result = new List<TrackedObject>(colliderMap.Count);
        foreach (var kv in colliderMap)
        {
            Transform t = kv.Key;
            List<Collider> cols = kv.Value;

            TrackedObject entry = new TrackedObject
            {
                transform = t,
                name = t.name,
                id = t.GetInstanceID(),
                colliders = cols.ToArray(),
                isCarried = carryStateMap.TryGetValue(t, out bool carried) && carried
            };

            result.Add(entry);
        }

        result.Sort((a, b) =>
        {
            string aKey = a.name + "#" + a.id;
            string bKey = b.name + "#" + b.id;
            return string.CompareOrdinal(aKey, bKey);
        });

        return result.ToArray();
    }

    string EvaluateCollisionState(Collider[] aColliders, Collider[] bColliders)
    {
        bool hasA = false;
        bool hasB = false;
        bool hasIgnoredPair = false;

        for (int i = 0; i < aColliders.Length; i++)
        {
            Collider a = aColliders[i];
            if (!IsUsableCollider(a))
                continue;

            hasA = true;

            for (int j = 0; j < bColliders.Length; j++)
            {
                Collider b = bColliders[j];
                if (!IsUsableCollider(b))
                    continue;

                hasB = true;

                if (Physics.GetIgnoreCollision(a, b))
                {
                    hasIgnoredPair = true;
                    continue;
                }

                if (!a.bounds.Intersects(b.bounds))
                    continue;

                if (Physics.ComputePenetration(
                        a, a.transform.position, a.transform.rotation,
                        b, b.transform.position, b.transform.rotation,
                        out Vector3 direction, out float distance))
                {
                    if (distance > 0.0001f)
                        return "colliding";
                }

                if (a.bounds.Intersects(b.bounds))
                    return "bounds-overlap";
            }
        }

        if (!hasA || !hasB)
            return "no-collider";

        if (hasIgnoredPair)
            return "ignored";

        return "none";
    }

    static bool IsUsableCollider(Collider c)
    {
        return c != null && c.enabled && c.gameObject.activeInHierarchy;
    }

    static string FormatVec(Vector3 v)
    {
        return v.x.ToString("F4") + "," + v.y.ToString("F4") + "," + v.z.ToString("F4");
    }

    static Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
    }

    static float NormalizeAngle(float degrees)
    {
        return Mathf.Repeat(degrees + 180f, 360f) - 180f;
    }
}