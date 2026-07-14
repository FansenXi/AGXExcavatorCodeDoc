using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  [DisallowMultipleComponent]
  public class YuLongMechanicalReset : AgxMachineMechanicalReset
  {
    [SerializeField] private ExcavatorYuLong m_yuLong = null;
    [SerializeField] private ExcavatorMachineController m_machineController = null;
    public override string ResetDisplayName => "YuLong";

    public override void CaptureInitialState()
    {
      ResolveReferences();
      m_yuLong?.ResolveReferences();
      base.CaptureInitialState();
    }
    public override void PrepareForReset() { ResolveReferences(); m_machineController?.StopMotion(); base.PrepareForReset(); }
    public override void RestoreInitialState() { ResolveReferences(); base.RestoreInitialState(); m_machineController?.StopMotion(); }
    public override void FinalizeAfterReset() { ResolveReferences(); base.FinalizeAfterReset(); m_machineController?.StopMotion(); }
    protected override bool ShouldRebindHingeAndPrismaticConstraints() { return false; }
    protected override bool ShouldEnableLockControllersAfterReset() { return false; }
    protected override bool ShouldLockTargetSpeedAtZero() { return false; }
    private void Reset() { ResolveReferences(); }
    private void Awake() { ResolveReferences(); }

    private void ResolveReferences()
    {
      if ( m_yuLong == null ) m_yuLong = GetComponent<ExcavatorYuLong>();
      if ( m_yuLong == null ) m_yuLong = GetComponentInChildren<ExcavatorYuLong>( true );
      if ( m_machineController == null ) m_machineController = FindObjectOfType<ExcavatorMachineController>();
    }
  }
}
