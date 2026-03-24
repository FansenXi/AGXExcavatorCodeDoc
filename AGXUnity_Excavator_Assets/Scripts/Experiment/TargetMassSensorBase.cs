using AGXUnity;
using UnityEngine;

public abstract class TargetMassSensorBase : ScriptComponent
{
  public abstract string TargetName { get; }
  public abstract float MassInBox { get; }
  public abstract float DepositedMass { get; }

  public abstract bool TryGetMeasurementVolume( out Transform measurementFrame,
                                                out Vector3 measurementCenterLocal,
                                                out Vector3 measurementHalfExtents );

  public abstract void ResetMeasurements();
}
