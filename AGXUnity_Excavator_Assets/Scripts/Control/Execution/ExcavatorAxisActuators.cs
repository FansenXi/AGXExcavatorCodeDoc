using System;
using AGXUnity;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  public enum ExcavatorAxisActuatorBackend
  {
    TargetSpeed,
    Hydraulic
  }

  internal interface IExcavatorAxisActuator
  {
    void Apply( float command, bool immediateStop );
  }

  internal sealed class TargetSpeedConstraintAxisActuator : IExcavatorAxisActuator
  {
    private const float ZeroSpeedThreshold = 1.0e-4f;

    private readonly Constraint[] m_constraints = null;
    private readonly float m_maxAcceleration = 0.0f;
    private readonly Func<float> m_deltaTimeProvider = null;

    public TargetSpeedConstraintAxisActuator( Constraint constraint,
                                              float maxAcceleration,
                                              Func<float> deltaTimeProvider )
      : this( constraint != null ? new[] { constraint } : null, maxAcceleration, deltaTimeProvider )
    {
    }

    public TargetSpeedConstraintAxisActuator( Constraint[] constraints,
                                              float maxAcceleration,
                                              Func<float> deltaTimeProvider )
    {
      m_constraints = constraints ?? new Constraint[0];
      m_maxAcceleration = maxAcceleration;
      m_deltaTimeProvider = deltaTimeProvider;
    }

    public void Apply( float command, bool immediateStop )
    {
      var referenceConstraint = GetReferenceConstraint();
      if ( referenceConstraint == null )
        return;

      var currentSpeed = referenceConstraint.GetCurrentSpeed();
      var newSpeed = CalculateSpeed( command, currentSpeed, m_maxAcceleration, GetDeltaTime() );
      foreach ( var constraint in m_constraints )
        SetSpeed( constraint, newSpeed, immediateStop );
    }

    private Constraint GetReferenceConstraint()
    {
      foreach ( var constraint in m_constraints ) {
        if ( constraint != null )
          return constraint;
      }

      return null;
    }

    private float GetDeltaTime()
    {
      return m_deltaTimeProvider != null ? m_deltaTimeProvider() : Time.deltaTime;
    }

    private static float CalculateSpeed( float desiredSpeed, float currentSpeed, float maxAcceleration, float deltaTime )
    {
      var maxDeltaSpeed = Mathf.Abs( maxAcceleration * Mathf.Max( deltaTime, 0.0f ) );
      return Mathf.Clamp( desiredSpeed, currentSpeed - maxDeltaSpeed, currentSpeed + maxDeltaSpeed );
    }

    private static void SetSpeed( Constraint constraint, float speed, bool immediateStop )
    {
      if ( constraint == null )
        return;

      var speedController = constraint.GetController<TargetSpeedController>();
      if ( speedController == null )
        return;

      var lockController = constraint.GetController<LockController>();

      if ( immediateStop && Mathf.Abs( speed ) < ZeroSpeedThreshold ) {
        speedController.Speed = 0.0f;
        speedController.LockAtZeroSpeed = false;

        if ( lockController != null ) {
          speedController.Enable = false;
          lockController.Position = constraint.GetCurrentAngle();
          lockController.Enable = true;
        }
        else {
          speedController.Enable = true;
        }

        return;
      }

      speedController.LockAtZeroSpeed = false;
      speedController.Enable = true;
      speedController.Speed = Mathf.Abs( speed ) < ZeroSpeedThreshold ? 0.0f : speed;
      if ( lockController != null )
        lockController.Enable = false;
    }
  }
}
