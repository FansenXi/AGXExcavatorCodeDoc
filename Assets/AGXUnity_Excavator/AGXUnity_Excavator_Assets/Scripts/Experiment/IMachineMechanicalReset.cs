namespace AGXUnity_Excavator.Scripts.Experiment
{
  public interface IMachineMechanicalReset
  {
    string ResetDisplayName { get; }
    void CaptureInitialState();
    void PrepareForReset();
    void RestoreInitialState();
    void FinalizeAfterReset();
  }
}
