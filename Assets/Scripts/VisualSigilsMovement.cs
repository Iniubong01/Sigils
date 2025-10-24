using UnityEngine;
using System.Collections;
using TMPro;
using UnityEngine.UI;

public class VisualSigilsMovement : MonoBehaviour
{
    [Header("Drift Settings")]
    public float driftSpeed = 0.5f;
    public float driftDuration = 120f;
    public float bobIntensity = 0.3f;
    public float bobSpeed = 1f;

    public bool IsIdling { get; private set; } = false;
    public bool hasBeenReleasedBefore = false;

    public System.Action OnEnterIdlePhase;

    [Header("Text Reference")]
    public TMP_Text descriptionText; // descriptionText 
    public bool canChangeThisText = true;

    private Vector3 currentDirection;
    private Coroutine driftCoroutine;

    void Start()
    {
        currentDirection = Random.onUnitSphere;
        currentDirection.y = Mathf.Abs(currentDirection.y) * 0.3f;
        currentDirection = currentDirection.normalized;
        
        driftCoroutine = StartCoroutine(DriftAndIdle());

        // Disable text changes after a short delay (once text is set from EmotionSigilSO)
        StartCoroutine(DisableTextChanges());
    }

    IEnumerator DisableTextChanges()
    {
        // Wait a moment for EmotionSigilSO to set the text, then lock it
        yield return new WaitForSeconds(1f);
        canChangeThisText = false;
    }

    // Public method to set the text (called from EmotionSigilSO)
    public void SetText(string label, string description)
    {
        if (!canChangeThisText) return;
 
        if (descriptionText != null)
            descriptionText.text = description;
    }

    IEnumerator DriftAndIdle()
    {
        float elapsed = 0f;

        while (elapsed < driftDuration && !IsIdling)
        {
            ApplyBobMotion();
            transform.position += currentDirection * driftSpeed * Time.deltaTime;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!IsIdling)
        {
            EnterIdlePhaseImmediately();
        }
    }

    public void EnterIdlePhaseImmediately()
    {
        if (IsIdling) return;

        IsIdling = true;
        
        if (driftCoroutine != null)
        {
            StopCoroutine(driftCoroutine);
            driftCoroutine = null;
        }
        
        Debug.Log($"{gameObject.name} entering idle phase");
        
        OnEnterIdlePhase?.Invoke();

        StartCoroutine(IdleBobbing());
    }

    IEnumerator IdleBobbing()
    {
        while (true)
        {
            ApplyBobMotion();
            yield return null;
        }
    }

    void ApplyBobMotion()
    {
        float time = Time.time;
        float x = Mathf.Sin(time * bobSpeed * 0.7f) * bobIntensity * 0.1f;
        float y = Mathf.Sin(time * bobSpeed) * bobIntensity * 0.2f;
        float z = Mathf.Cos(time * bobSpeed * 0.8f) * bobIntensity * 0.1f;

        transform.position += new Vector3(x, y, z) * Time.deltaTime;
    }
}