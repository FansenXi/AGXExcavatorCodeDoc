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
      get { return ResolveConstraint( ref m_swingHinge, "joint1", "base_to_controller_joint" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BoomConstraint
    {
      get { return ResolveConstraint( ref m_boomConstraint, "joint2", "controller_to_dabi_joint" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BoomCylinderPrismatic
    {
      get { return ResolveConstraint( ref m_boomCylinderPrismatic, "boom_cylinder_prismatic" ); }
    }

    [AllowRecursiveEditing]
    public Constraint StickConstraint
    {
      get { return ResolveConstraint( ref m_stickConstraint, "joint3", "dabi_to_xiaobi_joint" ); }
    }

    [AllowRecursiveEditing]
    public Constraint StickCylinderPrismatic
    {
      get { return ResolveConstraint( ref m_stickCylinderPrismatic, "stick_cylinder_prismatic" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BucketConstraint
    {
      get { return ResolveConstraint( ref m_bucketConstraint, "joint4", "xiaobi_to_watou_joint" ); }
    }

    [AllowRecursiveEditing]
    public Constraint BucketCylinderPrismatic
    {
      get { return ResolveConstraint( ref m_bucketCylinderPrismatic, "bucket_cylinder_prismatic" ); }
    }

	    [AllowRecursiveEditing]
	    public Transform BucketReference
	    {
	      get { return ResolveTransform( ref m_bucketReference, "bucket", "Bucket", "watou" ); }
	    }

    public float Speed
    {
      get { return 0.0f; }
    }

    public void ResolveReferences()
    {
      ResolveConstraint( ref m_swingHinge, "joint1", "base_to_controller_joint" );
      ResolveConstraint( ref m_boomConstraint, "joint2", "controller_to_dabi_joint" );
      ResolveConstraint( ref m_boomCylinderPrismatic, "boom_cylinder_prismatic" );
      ResolveConstraint( ref m_stickConstraint, "joint3", "dabi_to_xiaobi_joint" );
      ResolveConstraint( ref m_stickCylinderPrismatic, "stick_cylinder_prismatic" );
      ResolveConstraint( ref m_bucketConstraint, "joint4", "xiaobi_to_watou_joint" );
      ResolveConstraint( ref m_bucketCylinderPrismatic, "bucket_cylinder_prismatic" );
	      ResolveTransform( ref m_bucketReference, "watou" );
    }

    private Constraint ResolveConstraint( ref Constraint constraint, params string[] objectNames )
    {
      if ( IsUsable( constraint ) )
        return constraint;

      foreach ( var objectName in objectNames ) {
        var target = FindChildRecursive( transform, objectName );
        constraint = target != null ? target.GetComponent<Constraint>() : null;
        if ( constraint != null )
          return constraint;
      }

      return constraint;
    }

	    private Transform ResolveTransform( ref Transform reference, params string[] objectNames )
	    {
	      if ( reference != null )
	        return reference;
	
	      foreach ( var objectName in objectNames ) {
	        reference = FindChildRecursive( transform, objectName );
	        if ( reference != null )
	          return reference;
	      }
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
