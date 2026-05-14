using AGXUnity;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Core
{
  [DisallowMultipleComponent]
  [AddComponentMenu( "AGXUnity/Excavator/YuLong Rig" )]
  public class ExcavatorYuLong : ScriptComponent
  {
    [SerializeField]
    private Constraint m_swingHinge = null;

    [SerializeField]
    private Constraint m_boomConstraint = null;

    [SerializeField]
    private Constraint m_stickConstraint = null;

    [SerializeField]
    private Constraint m_bucketConstraint = null;

    [SerializeField]
    private Transform m_bucketReference = null;

    [AllowRecursiveEditing]
    public Constraint SwingHinge
    {
      get { return ResolveConstraint( ref m_swingHinge, "joint1" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BoomConstraint
    {
      get { return ResolveConstraint( ref m_boomConstraint, "joint2" ); }
    }

    [AllowRecursiveEditing]
    public Constraint StickConstraint
    {
      get { return ResolveConstraint( ref m_stickConstraint, "joint3" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BucketConstraint
    {
      get { return ResolveConstraint( ref m_bucketConstraint, "joint4" ); }
    }

    [AllowRecursiveEditing]
    public Transform BucketReference
    {
      get { return ResolveTransform( ref m_bucketReference, "watou" ); }
    }

    public float Speed
    {
      get { return 0.0f; }
    }

    public void ResolveReferences()
    {
      ResolveConstraint( ref m_swingHinge, "joint1" );
      ResolveConstraint( ref m_boomConstraint, "joint2" );
      ResolveConstraint( ref m_stickConstraint, "joint3" );
      ResolveConstraint( ref m_bucketConstraint, "joint4" );
      ResolveTransform( ref m_bucketReference, "watou" );
    }

    private Constraint ResolveConstraint( ref Constraint constraint, string objectName )
    {
      if ( IsUsable( constraint ) )
        return constraint;

      var target = FindChildRecursive( transform, objectName );
      constraint = target != null ? target.GetComponent<Constraint>() : null;
      return constraint;
    }

    private Transform ResolveTransform( ref Transform reference, string objectName )
    {
      if ( reference != null )
        return reference;

      reference = FindChildRecursive( transform, objectName );
      return reference;
    }

    private static bool IsUsable( Component component )
    {
      return component != null;
    }

    private static Transform FindChildRecursive( Transform root, string objectName )
    {
      if ( root == null )
        return null;

      if ( root.name == objectName )
        return root;

      foreach ( Transform child in root ) {
        var result = FindChildRecursive( child, objectName );
        if ( result != null )
          return result;
      }

      return null;
    }
  }
}
