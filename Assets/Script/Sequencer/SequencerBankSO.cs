using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Sequencer Bank", menuName = "Music Therapy/Sequencer Bank")]
public class SequencerBankSO : ScriptableObject
{
    public List<SequencerButtonDefinition> buttons = new List<SequencerButtonDefinition>();
}
