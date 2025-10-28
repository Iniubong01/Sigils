using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Linq;
using System.IO;

  // Before I start I love this "#region && #endregion" "preprocessor directives"
  // - It helps me organize my methods and I can easily see the regions from the "minimap" on the right
[System.Serializable]
public class EmotionData
{
    public string emotionName;
    public Vector3 position;
    public bool isReleased;
    public int emotionType;
    public string sessionID;
    public string emotionLabel;
    public string emotionDescription;
}

[System.Serializable]
public class ContainerData   // For the parent container
{
    public string sessionID;
    public Vector3 position;
}

[System.Serializable]
public class SaveData
{
    public string sessionID;
    public float releaseCounter;
    public bool isSessionCompleted;
    public List<EmotionData> emotions = new List<EmotionData>();
    public List<ContainerData> containers = new List<ContainerData>(); // NEW
}

public class EmotionSigilSO : MonoBehaviour
{
    public enum EmotionType { Happiness, Sadness, Worry, Calm, Anger }

    [Header("Current Emotion")]
    public EmotionType currentEmotion;

    [Header("UI References")]
    [SerializeField] private TMP_Dropdown emotionDropdown;
    [SerializeField] private TMP_InputField emotionInput;
    [SerializeField] private TMP_InputField sessionIDInput;
    [SerializeField] private Button releaseButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button newSessionButton;
    [SerializeField] private TMP_Text counterText;
    [SerializeField] private TMP_Text sessionStatusText;

    [Tooltip("Sigils (emotion objects) on the stand"), Header("Stand Emotion Objects (Shown on Dropdown)")]
    [SerializeField] private GameObject[] emotionVisuals;

    [Tooltip("Sigils (emotion objects) prefabs to be spawned"), Header("Floating Emotion Prefabs (Spawned on Release)")]
    [SerializeField] private GameObject[] emotionPrefabs;

    [Tooltip("The transparent parent prefab, that acts as a parent grouper"), Header("Parent Container Prefab")]   
    [SerializeField] private GameObject leaderContainerPrefab;  // The transparent parent prefab, that acts as a parent grouper

    [Header("Follower Settings")]
    [SerializeField] private float followSmoothness = 2.0f;
    [SerializeField] private float minFollowDistance = 2.0f;
    [SerializeField] private float maxFollowDistance = 3.5f;
    [SerializeField] private float heightVariation = 0.3f;
    [Tooltip("This controls how close the objects are the leader of that session"), SerializeField] private float closeRange = 1.5f, closeRadius;  /// This controls how close the objects are the leader of that session

    private float releaseCounter = 0;
    private bool isSessionCompleted = false;
    private GameObject activeVisual;
    private Transform leader;
    private List<GameObject> releasedObjects = new List<GameObject>();
    private List<Vector3> followerDirections = new List<Vector3>();
    private List<bool> followerIsIdling = new List<bool>();
    private List<string> objectSessionIDs = new List<string>();
    private Dictionary<string, Transform> sessionLeaders = new Dictionary<string, Transform>();
    private Dictionary<string, List<int>> sessionFollowers = new Dictionary<string, List<int>>();
    private string savePath;
    private string currentSessionID;
    private List<GameObject> sessionContainers = new List<GameObject>();

    void Start()
    {
        savePath = Path.Combine(Application.persistentDataPath, "emotion_save.json");

        emotionDropdown.ClearOptions();
        emotionDropdown.AddOptions(System.Enum.GetNames(typeof(EmotionType)).ToList());
        emotionDropdown.onValueChanged.AddListener(OnEmotionChanged);

        releaseButton.onClick.AddListener(OnReleaseButtonPressed);
        resetButton.onClick.AddListener(ResetAllData);
        newSessionButton.onClick.AddListener(StartNewSession);

        emotionInput.onValueChanged.AddListener(CheckInputFields);
        sessionIDInput.onValueChanged.AddListener(CheckInputFields);

        CheckInputFields("");
        OnEmotionChanged(emotionDropdown.value);
        
        // INITIALIZE THE CURRENT SESSION IN DICTIONARIES
        if (string.IsNullOrEmpty(currentSessionID))
        {
            currentSessionID = "001";
        }
        if (!sessionLeaders.ContainsKey(currentSessionID))
        {
            sessionLeaders[currentSessionID] = null;
        }
        if (!sessionFollowers.ContainsKey(currentSessionID))
        {
            sessionFollowers[currentSessionID] = new List<int>();
        }
        
        LoadData();
        
        UpdateSessionStatusUI();
        UpdateNewSessionButton();
    }

    void CheckInputFields(string _)
    {
        bool hasValidInput = !string.IsNullOrWhiteSpace(emotionInput.text) && 
                            !string.IsNullOrWhiteSpace(sessionIDInput.text);
        
        bool canRelease = hasValidInput && !isSessionCompleted && releaseCounter < 3;
        
        releaseButton.interactable = canRelease;
    }

    public void StartNewSession()
    {
        if (!isSessionCompleted && releaseCounter > 0)
        {
            Debug.LogWarning("Current session is not completed. Complete it or reset before starting a new session.");
            return;
        }

        string nextSessionID = GenerateNextSessionID();
        
        // Reset only current session tracking - and not clear the dictionaries
        releaseCounter = 0;
        isSessionCompleted = false;
        currentSessionID = nextSessionID;
        sessionIDInput.text = nextSessionID;
        
        // Only reset the current session's leader, not the entire system
        sessionLeaders[currentSessionID] = null;
        if (sessionFollowers.ContainsKey(currentSessionID))
        {
            sessionFollowers[currentSessionID].Clear();
        }
        else
        {
            sessionFollowers[currentSessionID] = new List<int>();
        }
        
        // Reset the current leader reference
        leader = null;

        Debug.Log($"Starting new session: {nextSessionID}");

        emotionInput.text = "";
        emotionDropdown.value = 0;
        OnEmotionChanged(0);

        SaveData();
        UpdateSessionStatusUI();
        UpdateNewSessionButton();

        Debug.Log($"New session {nextSessionID} started! You can now release 3 new emotions.");
    }

    private string GenerateNextSessionID()
    {
        if (string.IsNullOrEmpty(currentSessionID))
        {
            return "001";
        }

        if (int.TryParse(currentSessionID, out int currentSessionNumber))
        {
            int nextSessionNumber = currentSessionNumber + 1;
            return nextSessionNumber.ToString("D3");
        }
        else
        {
            return "001";
        }
    }

    private void UpdateNewSessionButton()
    {
        newSessionButton.interactable = isSessionCompleted;
        
        if (newSessionButton.GetComponentInChildren<TMP_Text>() != null)
        {
            if (isSessionCompleted)
            {
                newSessionButton.GetComponentInChildren<TMP_Text>().text = "Start New Session";
            }
            else
            {
                newSessionButton.GetComponentInChildren<TMP_Text>().text = "Complete Current Session First";
            }
        }
    }

    void OnEmotionChanged(int index)
    {
        currentEmotion = (EmotionType)index;

        for (int i = 0; i < emotionVisuals.Length; i++)
        {
            if (emotionVisuals[i] == null) continue;
            emotionVisuals[i].SetActive(i == index);
        }

        activeVisual = emotionVisuals[index];
        UpdateEmotionPrompt(currentEmotion);
    }

    void UpdateEmotionPrompt(EmotionType emotion)
    {
        if (emotionInput.placeholder is TMP_Text placeholder)
        {
            placeholder.text = emotion switch
            {
                EmotionType.Happiness => "What made you smile today...",
                EmotionType.Sadness => "What's weighing on your heart?",
                EmotionType.Worry => "What thoughts keep circling your mind?",
                EmotionType.Calm => "What brings you peace right now?",
                EmotionType.Anger => "What's burning inside you?", 
                _ => "How are you feeling?"
            };
        }
        emotionInput.text = "";
    }

    #region Release button functionality
    void OnReleaseButtonPressed()
    {
        if (isSessionCompleted)
        {
            Debug.LogWarning("Session already completed. Please start a new session to continue.");
            return;
        }

        int index = (int)currentEmotion;
        if (index < 0 || index >= emotionPrefabs.Length)
        {
            Debug.LogWarning("Emotion prefab not assigned properly!");
            return;
        }

        if (releaseCounter >= 3)
        {
            Debug.LogWarning("Max 3 emotions per session.");
            releaseButton.interactable = false;
            return;
        }

        GameObject prefab = emotionPrefabs[index];
        GameObject released = Instantiate(prefab, activeVisual.transform.position, Quaternion.identity);
        
        // SET THE TEXT ON THE PREFAB - Emotion enum as label, user input as description
        string emotionLabel = currentEmotion.ToString(); // Get the enum name
        string emotionDescription = emotionInput.text; // Get user's description
        SetPrefabText(released, emotionLabel, emotionDescription);
        
        VisualSigilsMovement movement = released.GetComponent<VisualSigilsMovement>();
        if (movement != null)
        {
            movement.hasBeenReleasedBefore = false;
        }

        releasedObjects.Add(released);
        objectSessionIDs.Add(currentSessionID);

        if (sessionLeaders[currentSessionID] == null)
        {
            sessionLeaders[currentSessionID] = released.transform;
            leader = released.transform;
            followerIsIdling.Add(false);
            Debug.Log($"New leader set for session {currentSessionID}: {released.name}");

            // Listen for leader idle event
            VisualSigilsMovement leaderMovement = released.GetComponent<VisualSigilsMovement>();
            if (leaderMovement != null)
            {
                leaderMovement.OnEnterIdlePhase += () => OnLeaderEnteredIdle(currentSessionID);
            }
        }
        else
        {
            Vector3 randomDirection = CreateRandomDirection();
            followerDirections.Add(randomDirection);
            followerIsIdling.Add(false);

            sessionFollowers[currentSessionID].Add(releasedObjects.Count - 1);

            if (movement != null)
            {
                int followerIndex = releasedObjects.Count - 1;
                movement.OnEnterIdlePhase += () => OnFollowerEnteredIdle(followerIndex);
            }
            Debug.Log($"New follower added for session {currentSessionID}: {released.name}");
        }

        releaseCounter++;

        if (releaseCounter >= 3)
        {
            isSessionCompleted = true;
            UpdateNewSessionButton();
        }

        SaveData();
        UpdateSessionStatusUI();
    }

    void SetPrefabText(GameObject prefabInstance, string emotionLabel, string emotionDescription)
    {
        VisualSigilsMovement movement = prefabInstance.GetComponent<VisualSigilsMovement>();
        if (movement != null && movement.canChangeThisText)
        {
            // Use the SetText method from VisualSigilsMovement which respects the bool
            movement.SetText(emotionLabel, emotionDescription);
        }
        else if (movement != null)
        {
            Debug.LogWarning($"Cannot set text on {prefabInstance.name} - text changes are disabled");
        }
        else
        {
            Debug.LogWarning("VisualSigilsMovement script is not found on prefab");
        }
    }

    void OnFollowerEnteredIdle(int followerIndex)
    {
        if (followerIndex > 0 && followerIndex - 1 < followerIsIdling.Count)
        {
            followerIsIdling[followerIndex - 1] = true;
            Debug.Log($"Follower {followerIndex} stopped following - now idling");
        }
    }

    void OnLeaderEnteredIdle(string sessionID)
    {
        if (string.IsNullOrEmpty(sessionID))
        {
            Debug.LogWarning("OnLeaderEnteredIdle called with null or empty sessionID.");
            return;
        }

        if (!sessionLeaders.ContainsKey(sessionID) || sessionLeaders[sessionID] == null)
        {
            Debug.LogWarning($"No leader found for session {sessionID}. Cannot create container.");
            return;
        }

        Transform leaderTransform = sessionLeaders[sessionID];

        // Find existing container by name (if previously created/saved)
        GameObject existingContainer = sessionContainers.Find(
            c => c != null && c.name == $"LeaderContainer_{sessionID}"
        );

        GameObject container;

        if (existingContainer != null)
        {
            container = existingContainer;
            // In case the saved container exists but was at a wrong position, snap it to leader now:
            container.transform.position = leaderTransform.position;
            Debug.Log($"Reusing existing container for session {sessionID}.");
        }
        else
        {
            if (leaderContainerPrefab == null)
            {
                Debug.LogWarning("Leader container prefab not assigned!");
                return;
            }

            container = Instantiate(leaderContainerPrefab, leaderTransform.position, Quaternion.identity);
            container.name = $"LeaderContainer_{sessionID}";

            TMP_Text idText = container.GetComponentInChildren<TMP_Text>();
            if (idText != null)
                idText.text = $"Session {sessionID}";

            sessionContainers.Add(container);
            Debug.Log($"Container spawned for session {sessionID} at {leaderTransform.position}");
        }

        // Parent followers to the container so they stay visually inside it.
        if (sessionFollowers.ContainsKey(sessionID))
        {
            foreach (int followerIndex in sessionFollowers[sessionID])
            {
                if (followerIndex >= 0 && followerIndex < releasedObjects.Count)
                {
                    GameObject follower = releasedObjects[followerIndex];
                    if (follower != null)
                        follower.transform.SetParent(container.transform, true);
                }
            }
        }

        // Note: DO NOT parent the leader to the container. Instead we make the container follow the leader every frame.
        // That keeps leader's own movement logic intact while visually grouping everything.
    }


    IEnumerator FollowLeader(Transform container, Transform leader)   /// Optional for smooth follow, but I figure fast parenting will be proper
    {
        while (container != null && leader != null)
        {
            container.position = Vector3.Lerp(container.position, leader.position, followSmoothness);
            yield return null;
        }
    }


    Vector3 CreateRandomDirection()
    {
        Vector3 randomPoint = Random.onUnitSphere;
        randomPoint.y *= heightVariation;

        if (randomPoint.y < -0.2f)
        {
            randomPoint.y = -randomPoint.y * 0.3f;
        }

        return randomPoint.normalized;
    }
    #endregion

    #region UPDATE AND FOLLOWER FUNCTIONALITY
    void Update()
    {
        counterText.text = releaseCounter.ToString("0");
        CheckInputFields("");

        UpdateFollowerPositions();

        UpdateContainersFollow(); // <-- keep containers following leaders

        if (Time.frameCount % 120 == 0)
        {
            SaveData();  // You can modify this whole if statement
        }
    }

    #region FOLLOWER LERP
    void UpdateContainersFollow()
    {
        // For every session container, find the matching leader and move container to leader.position
        for (int i = sessionContainers.Count - 1; i >= 0; i--)
        {
            var container = sessionContainers[i];
            if (container == null)
            {
                sessionContainers.RemoveAt(i);
                continue;
            }

            // extract sessionID from name
            string name = container.name; // "LeaderContainer_001"
            string sessionID = name.StartsWith("LeaderContainer_") ? name.Replace("LeaderContainer_", "") : null;
            if (string.IsNullOrEmpty(sessionID)) continue;

            if (sessionLeaders.TryGetValue(sessionID, out Transform leaderTransform) && leaderTransform != null)
            {
                // Smooth lerp follow, using followSmoothness as speed factor
                container.transform.position = Vector3.Lerp(container.transform.position,
                                                            leaderTransform.position, followSmoothness * Time.deltaTime);  // This line controls the follower's lerping to leader's position
            }
        }
    }
    #endregion

    void UpdateFollowerPositions()
    {
        foreach (var sessionPair in sessionLeaders)
        {
            string sessionID = sessionPair.Key;
            Transform sessionLeader = sessionPair.Value;
            if (sessionLeader == null) continue;

            if (!sessionFollowers.ContainsKey(sessionID)) continue;
            List<int> followers = sessionFollowers[sessionID];

            for (int i = 0; i < followers.Count; i++)
            {
                int followerIndex = followers[i];
                if (followerIndex >= releasedObjects.Count || releasedObjects[followerIndex] == null)
                    continue;

                GameObject follower = releasedObjects[followerIndex];
                VisualSigilsMovement movement = follower.GetComponent<VisualSigilsMovement>();

                if (movement == null) continue;  // Add function to stop updating follower's position in relation to it's session leader

                float distance = Vector3.Distance(follower.transform.position, sessionLeader.position);

                // Retrieve or create a offset for this follower
                if (i >= followerDirections.Count)
                    followerDirections.Add(Random.insideUnitSphere * 2f);

                Vector3 offset = followerDirections[i];

                // CHASE MODE (outside range)
                if (distance > maxFollowDistance)     /// This line keeps their following in check, initially I had this sticking and shaky aggressive follow
                {
                    Vector3 chaseTarget = sessionLeader.position + offset;
                    follower.transform.position = Vector3.Lerp(follower.transform.position,
                                                                chaseTarget,
                                                                followSmoothness * Time.deltaTime
                    );
                }
                else
                {
                    // --- FORMATION MODE (within range) ---
                    Vector3 formationTarget = GetTriangleFormationPosition(i, sessionLeader);
                    Vector3 bobOffset = new Vector3(
                        0f,
                        Mathf.Sin(Time.time * (1.5f + i * 0.3f)) * heightVariation,
                        0f
                    );

                    Vector3 desiredPos = formationTarget + bobOffset;

                    float distToTarget = Vector3.Distance(follower.transform.position, desiredPos);

                    // Only move if significantly far from desired position
                    if (distToTarget > closeRadius)
                    {
                        // Move smoothly toward the desired position without overshooting or jitter
                        follower.transform.position = Vector3.MoveTowards(
                            follower.transform.position,
                            desiredPos,
                            followSmoothness * Time.deltaTime
                        );
                    }
                    else
                    {
                        // Snap perfectly into place when within the threshold to prevent wiggle
                        follower.transform.position = Vector3.Lerp(follower.transform.position, desiredPos, followSmoothness * Time.deltaTime);  // smmooth out into position around player
                        // follower.transform.position = desiredPos; // Initially I had this, which was causing a terrible fast snap. Was thinking of using dotween for this tho, but that will need more time, so we'll make do with this
                    }
                }
            }
        }
    }


    #region POSITION AROUND LEADER
    // Returns smooth triangular positions around the leader
    Vector3 GetTriangleFormationPosition(int index, Transform leader)
    {
        // "closeRange" directly affects spacing — higher = looser formation, lower = tighter
        float spacing = closeRange + (index / 3) * 0.5f;
        int posInTri = index % 3;

        Vector3 right = leader.right * spacing;
        Vector3 forward = leader.forward * spacing;

        switch (posInTri)
        {
            case 0: return leader.position + (-right + forward);
            case 1: return leader.position + (right + forward);
            default: return leader.position + (-forward * 1.2f);
        }
    }
    #endregion


    int GetFollowerDirectionIndex(int followerIndex, string sessionID)
    {
        int directionIndex = 0;
        for (int i = 0; i < followerIndex; i++)
        {
            if (i < objectSessionIDs.Count && objectSessionIDs[i] == sessionID && i > 0)
            {
                directionIndex++;
            }
        }
        return directionIndex - 1;
    }
    #endregion

    void UpdateSessionStatusUI()
    {
        if (sessionStatusText != null)
        {
            if (isSessionCompleted)
            {
                sessionStatusText.text = $"Session {currentSessionID} Completed!";
                sessionStatusText.color = Color.green;
            }
            else
            {
                sessionStatusText.text = $"Session {currentSessionID} - {releaseCounter}/3";
                sessionStatusText.color = Color.black;
            }
        }
    }

    #region Reset Data
    public void ResetAllData()
    {
        // Delete the save file if it exists
        if (File.Exists(savePath)) File.Delete(savePath);

        releaseCounter = 0;
        isSessionCompleted = false;
        leader = null;
        currentSessionID = "001";

        // CLEAR ALL EMOTION OBJECTS ---
        foreach (var obj in releasedObjects)
        {
            if (obj != null)
            {
                VisualSigilsMovement movement = obj.GetComponent<VisualSigilsMovement>();
                if (movement != null)
                    movement.OnEnterIdlePhase = null; // clear event to avoid dangling references
                Destroy(obj);
            }
        }
        releasedObjects.Clear();
        objectSessionIDs.Clear();
        followerDirections.Clear();
        followerIsIdling.Clear();

        //  Clear all session containers (the transparent parents) ---
        foreach (var container in sessionContainers)
        {
            if (container != null)
                Destroy(container);
        }
        sessionContainers.Clear(); // clear the list after destruction

        // RESET DICTIONARIES
        sessionLeaders.Clear();
        sessionFollowers.Clear();

        // Re-initialize base session after clearing
        sessionLeaders[currentSessionID] = null;
        sessionFollowers[currentSessionID] = new List<int>();

        // UI RESET ---
        foreach (var stand in emotionVisuals)
            stand.SetActive(false);

        emotionInput.text = "";
        sessionIDInput.text = "001";
        emotionDropdown.value = 0;
        OnEmotionChanged(0);
        releaseButton.interactable = false;

        UpdateSessionStatusUI();
        UpdateNewSessionButton();

        Debug.Log("Reset complete — back to emotional zero point (containers cleared too!)");

        LoadData();
    }
    #endregion

    #region JSON Settings
    void SaveData()
    {
        var data = new SaveData
        {
            sessionID = currentSessionID,
            releaseCounter = releaseCounter,
            isSessionCompleted = isSessionCompleted,
            emotions = new List<EmotionData>(),
            containers = new List<ContainerData>()
        };

        // Save emotions (already existing code)
        for (int i = 0; i < releasedObjects.Count; i++)
        {
            if (releasedObjects[i] == null) continue;

            string emotionDescription = GetPrefabDescriptionText(releasedObjects[i]);
            var emotionData = new EmotionData
            {
                position = releasedObjects[i].transform.position,
                emotionName = releasedObjects[i].name,
                sessionID = objectSessionIDs[i],
                emotionDescription = emotionDescription,
                emotionType = (int)GetEmotionTypeFromName(releasedObjects[i].name)
            };

            data.emotions.Add(emotionData);
        }

        // Save containers
        foreach (var container in sessionContainers)
        {
            if (container == null) continue;

            ContainerData cData = new ContainerData
            {
                sessionID = container.name.Replace("LeaderContainer_", ""),
                position = container.transform.position
            };

            data.containers.Add(cData);
        }

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(savePath, json);
    }

    EmotionType GetEmotionTypeFromName(string name)
    {
        foreach (EmotionType type in System.Enum.GetValues(typeof(EmotionType)))
            if (name.Contains(type.ToString()))
                return type;
        return EmotionType.Calm;
    }

    string GetPrefabDescriptionText(GameObject prefabInstance)
    {
        VisualSigilsMovement movement = prefabInstance.GetComponent<VisualSigilsMovement>();
        if (movement != null && movement.descriptionText != null)
        {
            return movement.descriptionText.text;
        }
        return string.Empty;
    }
    void LoadData()
    {
        if (!File.Exists(savePath)) return;

        try
        {
            string json = File.ReadAllText(savePath);
            var data = JsonUtility.FromJson<SaveData>(json);

            currentSessionID = data.sessionID;
            sessionIDInput.text = currentSessionID;
            releaseCounter = data.releaseCounter;
            isSessionCompleted = data.isSessionCompleted;

            // Clear only the released sigils and related runtime lists (do NOT clear sessionContainers)
            foreach (var obj in releasedObjects)
                if (obj != null) Destroy(obj);
            releasedObjects.Clear();
            objectSessionIDs.Clear();
            followerDirections.Clear();
            followerIsIdling.Clear();
            sessionLeaders.Clear();
            sessionFollowers.Clear();

            // RE-INITIALIZE THE CURRENT SESSION AFTER CLEARING
            if (!sessionLeaders.ContainsKey(currentSessionID))
            {
                sessionLeaders[currentSessionID] = null;
            }
            if (!sessionFollowers.ContainsKey(currentSessionID))
            {
                sessionFollowers[currentSessionID] = new List<int>();
            }

            // Recreate all released emotions from save
            foreach (var emotionData in data.emotions)
            {
                if (emotionData.emotionType >= 0 && emotionData.emotionType < emotionPrefabs.Length)
                {
                    GameObject prefab = emotionPrefabs[emotionData.emotionType];
                    GameObject released = Instantiate(prefab, emotionData.position, Quaternion.identity);

                    SetPrefabText(released, emotionData.emotionLabel, emotionData.emotionDescription);

                    releasedObjects.Add(released);
                    objectSessionIDs.Add(emotionData.sessionID);

                    VisualSigilsMovement movement = released.GetComponent<VisualSigilsMovement>();
                    if (movement != null)
                    {
                        movement.hasBeenReleasedBefore = (emotionData.sessionID != currentSessionID);
                    }
                }
            }

            // Rebuild containers from save (do NOT destroy existing containers — reset handles that)
            foreach (var cData in data.containers)
            {
                if (leaderContainerPrefab == null)
                    continue;

                GameObject container = Instantiate(leaderContainerPrefab, cData.position, Quaternion.identity);
                container.name = $"LeaderContainer_{cData.sessionID}";

                TMP_Text idText = container.GetComponentInChildren<TMP_Text>();
                if (idText != null)
                    idText.text = $"Session {cData.sessionID}";

                sessionContainers.Add(container); // keep track so SaveData can pick it up later
            }

            // Rebuild leader/follower mappings (leaders come from the instantiated releasedObjects)
            for (int i = 0; i < releasedObjects.Count; i++)
            {
                string sessionID = objectSessionIDs[i];

                // ENSURE THE SESSION EXISTS IN DICTIONARIES
                if (!sessionLeaders.ContainsKey(sessionID))
                {
                    sessionLeaders[sessionID] = null;
                }
                if (!sessionFollowers.ContainsKey(sessionID))
                {
                    sessionFollowers[sessionID] = new List<int>();
                }

                if (sessionLeaders[sessionID] == null)
                {
                    sessionLeaders[sessionID] = releasedObjects[i].transform;
                    followerIsIdling.Add(false);
                }
                else
                {
                    Vector3 directionToLeader = (releasedObjects[i].transform.position - sessionLeaders[sessionID].position).normalized;
                    followerDirections.Add(directionToLeader);
                    followerIsIdling.Add(false);

                    sessionFollowers[sessionID].Add(i);

                    VisualSigilsMovement movement = releasedObjects[i].GetComponent<VisualSigilsMovement>();
                    if (movement != null)
                    {
                        int followerIndex = i;
                        movement.OnEnterIdlePhase += () => OnFollowerEnteredIdle(followerIndex);
                    }
                }
            }

            if (sessionLeaders.ContainsKey(currentSessionID))
            {
                leader = sessionLeaders[currentSessionID];
            }

            Debug.Log($"Loaded session {data.sessionID} with {releasedObjects.Count} emotions");
            UpdateSessionStatusUI();
            UpdateNewSessionButton();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading save data: {e.Message}");
            releaseCounter = 0;
            isSessionCompleted = false;
        }
    }

    #endregion
}