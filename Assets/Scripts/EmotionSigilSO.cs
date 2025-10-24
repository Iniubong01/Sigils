using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Linq;
using System.IO;

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
public class SaveData
{
    public string sessionID;
    public float releaseCounter;
    public bool isSessionCompleted;
    public List<EmotionData> emotions = new List<EmotionData>();
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

    [Header("Stand Emotion Objects (Shown on Dropdown)")]
    [SerializeField] private GameObject[] emotionVisuals;

    [Header("Floating Emotion Prefabs (Spawned on Release)")]
    [SerializeField] private GameObject[] emotionPrefabs;

    [Header("Follower Settings")]
    [SerializeField] private float followSmoothness = 2.0f;
    [SerializeField] private float minFollowDistance = 2.0f;
    [SerializeField] private float maxFollowDistance = 3.5f;
    [SerializeField] private float heightVariation = 0.3f;
    [SerializeField] private float noEntryRadius = 1.5f;

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
            Debug.LogWarning("VisualSigilsMovement component not found on prefab");
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

    #region Update and Follower Functionality
    void Update()
    {
        counterText.text = releaseCounter.ToString("0");
        CheckInputFields("");

        UpdateFollowerPositions();

        if (Time.frameCount % 120 == 0)
        {
            SaveData();
        }
    }

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
                if (movement == null || movement.IsIdling) continue;

                // Calculate distance and get direction toward leader
                float distance = Vector3.Distance(follower.transform.position, sessionLeader.position);

                // Retrieve or create a persistent offset for this follower
                if (i >= followerDirections.Count)
                    followerDirections.Add(Random.insideUnitSphere * 2f);

                Vector3 offset = followerDirections[i];

                // --- CHASE MODE (outside range) ---
                if (distance > maxFollowDistance)
                {
                    Vector3 chaseTarget = sessionLeader.position + offset;
                    follower.transform.position = Vector3.Lerp(
                        follower.transform.position,
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
                    follower.transform.position = Vector3.Lerp(
                        follower.transform.position,
                        desiredPos,
                        followSmoothness * Time.deltaTime
                    );
                }
            }
        }
    }

// Returns smooth triangular positions around the leader
Vector3 GetTriangleFormationPosition(int index, Transform leader)
{
    // Index 0,1,2 form a triangle
    // If more than 3 followers appear, they wrap to next ring automatically
    float spacing = minFollowDistance + (index / 3) * 0.5f;
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

    public void ResetAllData()
    {
        if (File.Exists(savePath)) File.Delete(savePath);
        releaseCounter = 0;
        isSessionCompleted = false;
        leader = null;
        currentSessionID = "001";

        foreach (var obj in releasedObjects)
        {
            if (obj != null)
            {
                VisualSigilsMovement movement = obj.GetComponent<VisualSigilsMovement>();
                if (movement != null)
                    movement.OnEnterIdlePhase = null;
                Destroy(obj);
            }
        }

        releasedObjects.Clear();
        objectSessionIDs.Clear();
        followerDirections.Clear();
        followerIsIdling.Clear();
        sessionLeaders.Clear();
        sessionFollowers.Clear();

        // 🧩 FIX: Re-initialize base session after clearing
        sessionLeaders[currentSessionID] = null;
        sessionFollowers[currentSessionID] = new List<int>();

        foreach (var stand in emotionVisuals)
            stand.SetActive(false);

        emotionInput.text = "";
        sessionIDInput.text = "001";
        emotionDropdown.value = 0;
        OnEmotionChanged(0);
        releaseButton.interactable = false;

        UpdateSessionStatusUI();
        UpdateNewSessionButton();
        Debug.Log("Reset complete — back to emotional zero point");

        LoadData();
    }


    #region JSON Settings
    void SaveData()
    {
        var data = new SaveData
        {
            sessionID = currentSessionID,
            releaseCounter = releaseCounter,
            isSessionCompleted = isSessionCompleted,
            emotions = new List<EmotionData>()
        };

        for (int i = 0; i < releasedObjects.Count; i++)
        {
            if (releasedObjects[i] != null)
            {
                string emotionDescription = GetPrefabDescriptionText(releasedObjects[i]);

                var emotionData = new EmotionData
                {
                    position = releasedObjects[i].transform.position,
                    emotionName = releasedObjects[i].name,
                    sessionID = objectSessionIDs[i],
                    emotionDescription = emotionDescription
                };

                VisualSigilsMovement movement = releasedObjects[i].GetComponent<VisualSigilsMovement>();
                if (movement != null)
                {
                    emotionData.sessionID = objectSessionIDs[i];
                }

                foreach (EmotionType emotionType in System.Enum.GetValues(typeof(EmotionType)))
                {
                    if (releasedObjects[i].name.Contains(emotionType.ToString()))
                    {
                        emotionData.emotionType = (int)emotionType;
                        break;
                    }
                }

                data.emotions.Add(emotionData);
            }
        }

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(savePath, json);
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