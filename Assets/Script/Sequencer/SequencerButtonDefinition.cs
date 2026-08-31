using UnityEngine;

// Data-driven description of a single sequencer button. Sound design (which
// clip, which group, which mood) is authored entirely as data so new sounds
// can be added without touching code.
[System.Serializable]
public class SequencerButtonDefinition
{
    public string displayLabel;
    public string groupName;
    public string moodTag;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
}
