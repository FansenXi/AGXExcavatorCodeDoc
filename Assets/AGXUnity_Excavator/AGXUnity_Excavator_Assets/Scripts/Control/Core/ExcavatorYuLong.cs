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
    private Constraint m_boomCylinderPrismatic = null;

    [SerializeField]
    private Constraint m_stickConstraint = null;

    [SerializeField]
    private Constraint m_stickCylinderPrismatic = null;

    [SerializeField]
    private Constraint m_bucketConstraint = null;

    [SerializeField]
    private Constraint m_bucketCylinderPrismatic = null;

    [SerializeField]
    private Transform m_bucketReference = null;

    [AllowRecursiveEditing]
    public Constraint SwingHinge
    {
      get { return ResolveConstraint( ref m_swingHinge, "swing_joint" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BoomConstraint
    {
      get { return ResolveConstraint( ref m_boomConstraint, "boom_joint" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BoomCylinderPrismatic
    {
      get { return ResolveConstraint( ref m_boomCylinderPrismatic, "boom_cylinder_prismatic" ); }
    }

    [AllowRecursiveEditing]
    public Constraint StickConstraint
    {
      get { return ResolveConstraint( ref m_stickConstraint, "stick_joint" ); }
    }

    [AllowRecursiveEditing]
    public Constraint StickCylinderPrismatic
    {
      get { return ResolveConstraint( ref m_stickCylinderPrismatic, "stick_cylinder_prismatic" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BucketConstraint
    {
      get { return ResolveConstraint( ref m_bucketConstraint, "bucket_joint" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BucketCylinderPrismatic
    {
      get { return ResolveConstraint( ref m_bucketCylinderPrismatic, "bucket_cylinder_prismatic" ); }
    }

    [AllowRecursiveEditing]
    public Transform BucketReference
    {
      get { return ResolveTransform( ref m_bucketReference, "bucket" ); }
    }

    public float Speed
    {
      get { return 0.0f; }
    }

    public void ResolveReferences()
    {
      ResolveConstraint( ref m_swingHinge, "swing_joint" );
      ResolveConstraint( ref m_boomConstraint, "boom_joint" );
      ResolveConstraint( ref m_boomCylinderPrismatic, "boom_cylinder_prismatic" );
      ResolveConstraint( ref m_stickConstraint, "stick_joint" );
      ResolveConstraint( ref m_stickCylinderPrismatic, "stick_cylinder_prismatic" );
      ResolveConstraint( ref m_bucketConstraint, "bucket_joint" );
      ResolveConstraint( ref m_bucketCylinderPrismatic, "bucket_cylinder_prismatic" );
      ResolveTransform( ref m_bucketReference, "bucket" );
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
