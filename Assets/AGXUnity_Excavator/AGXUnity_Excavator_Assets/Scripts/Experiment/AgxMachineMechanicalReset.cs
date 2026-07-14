using System.Collections.Generic;
using AGXUnity;
using AGXUnity.Utils;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  public class AgxMachineMechanicalReset : MonoBehaviour, IMachineMechanicalReset
  {
    private sealed class BodySnapshot
    {
      public RigidBody Component = null;
      public agx.RigidBodyRef Native = null;
      public agx.AffineMatrix4x4 Transform;
      public agx.RigidBody.MotionControl MotionControl = agx.RigidBody.MotionControl.DYNAMICS;
    }

    [SerializeField] private string m_resetDisplayName = "AGX Machine";
    [SerializeField] private Transform m_resetRoot = null;
    [SerializeField] private bool m_lockHingeAndPrismaticConstraints = true;

    private readonly List<BodySnapshot> m_bodySnapshots = new List<BodySnapshot>();
    private readonly List<Constraint> m_constraints = new List<Constraint>();
    private readonly List<AGXUnity.Model.Track> m_tracks = new List<AGXUnity.Model.Track>();
    private bool m_hasInitialState = false;

    public virtual string ResetDisplayName => string.IsNullOrWhiteSpace( m_resetDisplayName ) ? name : m_resetDisplayName;

    public virtual void CaptureInitialState()
    {
      var root = ResolveResetRoot();
      m_bodySnapshots.Clear();
      m_constraints.Clear();
      m_tracks.Clear();
      if ( root == null )
        return;

      foreach ( var body in root.GetComponentsInChildren<RigidBody>( true ) ) {
        var initializedBody = body.GetInitialized<RigidBody>();
        if ( initializedBody?.Native == null )
          continue;
        var nativeBody = new agx.RigidBodyRef( initializedBody.Native );
        m_bodySnapshots.Add( new BodySnapshot {
          Component = body,
          Native = nativeBody,
          Transform = nativeBody.getTransform(),
          MotionControl = body.MotionControl
        } );
      }
      foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) )
        if ( constraint != null ) m_constraints.Add( constraint );
      foreach ( var track in root.GetComponentsInChildren<AGXUnity.Model.Track>( true ) )
        if ( track != null ) m_tracks.Add( track );
      CollectAdditionalConstraints( m_constraints );
      CollectAdditionalTracks( m_tracks );
      m_hasInitialState = m_bodySnapshots.Count > 0;
    }

    public virtual void PrepareForReset() { StopConstraintControllers(); }

    public virtual void RestoreInitialState()
    {
      if ( !m_hasInitialState )
        CaptureInitialState();
      foreach ( var snapshot in m_bodySnapshots ) {
        if ( snapshot?.Native == null )
          continue;
        snapshot.Native.setTransform( snapshot.Transform );
        snapshot.Native.setVelocity( 0.0, 0.0, 0.0 );
        snapshot.Native.setAngularVelocity( 0.0, 0.0, 0.0 );
        snapshot.Native.setForce( Vector3.zero.ToHandedVec3() );
        snapshot.Native.setTorque( Vector3.zero.ToHandedVec3() );
        if ( snapshot.Component != null )
          snapshot.Component.MotionControl = snapshot.MotionControl;
      }
      RebindConstraintsAtRestoredPose();
      ReinitializeTracks();
    }

    public virtual void FinalizeAfterReset() { StopConstraintControllers(); }
    protected virtual Transform ResolveResetRoot() { return m_resetRoot != null ? m_resetRoot : transform; }
    protected virtual void CollectAdditionalConstraints( ICollection<Constraint> constraints ) { }
    protected virtual void CollectAdditionalTracks( ICollection<AGXUnity.Model.Track> tracks ) { }
    protected virtual bool ShouldRebindHingeAndPrismaticConstraints() { return true; }
    protected virtual bool ShouldEnableLockControllersAfterReset() { return m_lockHingeAndPrismaticConstraints; }
    protected virtual bool ShouldLockTargetSpeedAtZero() { return true; }

    protected void StopConstraintControllers()
    {
      foreach ( var constraint in m_constraints ) {
        if ( constraint == null )
          continue;
        var speedController = constraint.GetController<TargetSpeedController>();
        if ( speedController != null ) {
          speedController.Speed = 0.0f;
          speedController.LockAtZeroSpeed = ShouldLockTargetSpeedAtZero();
          speedController.Enable = false;
        }
        var lockController = constraint.GetController<LockController>();
        if ( lockController != null && ShouldEnableLockControllersAfterReset() ) {
          lockController.Position = GetCurrentConstraintPosition( constraint );
          lockController.Enable = true;
        }
        else if ( lockController != null )
          lockController.Enable = false;
      }
    }

    private void RebindConstraintsAtRestoredPose()
    {
      if ( !ShouldRebindHingeAndPrismaticConstraints() ) {
        StopConstraintControllers();
        return;
      }
      foreach ( var constraint in m_constraints ) {
        if ( constraint?.Native == null )
          continue;
        if ( constraint.Type == ConstraintType.Prismatic ) {
          var prismatic = constraint.Native.asPrismatic();
          if ( prismatic == null ) continue;
          var lockedAtZero = prismatic.getMotor1D().getLockedAtZeroSpeed();
          if ( lockedAtZero ) prismatic.getMotor1D().setLockedAtZeroSpeed( false );
          prismatic.rebind();
          prismatic.getMotor1D().setSpeed( 0.0 );
          prismatic.getLock1D().setPosition( prismatic.getAngle() );
          if ( lockedAtZero ) prismatic.getMotor1D().setLockedAtZeroSpeed( true );
        }
        else if ( constraint.Type == ConstraintType.Hinge ) {
          var hinge = constraint.Native.asHinge();
          if ( hinge == null ) continue;
          var lockedAtZero = hinge.getMotor1D().getLockedAtZeroSpeed();
          if ( lockedAtZero ) hinge.getMotor1D().setLockedAtZeroSpeed( false );
          hinge.rebind();
          var hingeAngle = agx.RotationalAngle.safeCast( hinge.getAttachmentPair().getAngle( 0 ) );
          hingeAngle.setWindingNumber( 0 );
          hinge.getMotor1D().setSpeed( 0.0 );
          hinge.getLock1D().setPosition( hinge.getAngle() );
          if ( lockedAtZero ) hinge.getMotor1D().setLockedAtZeroSpeed( true );
        }
      }
    }

    private void ReinitializeTracks()
    {
      foreach ( var track in m_tracks ) {
        var initializedTrack = track?.GetInitialized<AGXUnity.Model.Track>();
        if ( initializedTrack?.Native == null ) continue;
        initializedTrack.Native.reset();
        initializedTrack.Native.setVelocity( Vector3.zero.ToHandedVec3() );
        initializedTrack.Native.setAngularVelocity( Vector3.zero.ToHandedVec3() );
        initializedTrack.Native.reinitialize( (ulong)Mathf.Max( initializedTrack.NumberOfNodes, 1 ), initializedTrack.Width,
                                             initializedTrack.Thickness, initializedTrack.InitialTensionDistance );
      }
    }

    private static float GetCurrentConstraintPosition( Constraint constraint )
    {
      return constraint != null ? constraint.GetCurrentAngle() : 0.0f;
    }
  }
}
