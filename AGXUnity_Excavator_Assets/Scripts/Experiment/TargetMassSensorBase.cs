using AGXUnity;

public abstract class TargetMassSensorBase : ScriptComponent
{
  public abstract string TargetName { get; }
  public abstract float MassInBox { get; }
  public abstract float DepositedMass { get; }

  public abstract void ResetMeasurements();
}
