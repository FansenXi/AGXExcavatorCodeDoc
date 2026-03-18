using UnityEngine;
using AGXUnity;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif


public class MassVolumeCounter : ScriptComponent
{
  [SerializeField]
  private bool m_listenForResetInput = false;

#if ENABLE_INPUT_SYSTEM
  private InputAction ResetAction;
#else
  public KeyCode ResetTerrainKey = KeyCode.R;
#endif

  public AGXUnity.Model.DeformableTerrainShovel shovel;
  public AGXUnity.Model.DeformableTerrain m_terrain;

  float m_massInBucket = 0;
  public float MassInBucket => m_massInBucket;


  protected override bool Initialize()
  {
    Debug.Assert( m_terrain );

#if ENABLE_INPUT_SYSTEM
    if ( m_listenForResetInput ) {
      ResetAction = new InputAction( "Reset", binding: "<Keyboard>/r" );
      ResetAction.Enable();
    }
#endif

    // Initialize the heights of the terrain
    ComputeTerrainHeights();

    return base.Initialize();

  }

  public void ResetMeasurements()
  {
    m_massInBucket = 0;

    if ( m_terrain != null )
      ComputeTerrainHeights();
  }


  /// <summary>
  /// Reset the terrain.
  /// Compute a new height for the terrain given some function
  /// </summary>
  void ComputeTerrainHeights()
  {
    m_terrain.ResetHeights();

    int resX = (int)m_terrain.Native.getResolutionX();
    int resY = (int)m_terrain.Native.getResolutionY();
    var heightData = new float[ resY, resX ];

    // compute new heights for the terrain data
    Vector2 center = new Vector2(resX/2, resY/2);
    for ( var x = 0; x < resX; x++ )
      for ( var y = 0; y < resY; y++ ) {
        var distance = (center - new Vector2(x, y)).magnitude*0.1f;
        var z = (1 / Mathf.Sqrt(2 * Mathf.PI)) * Mathf.Exp(-.5f * distance*distance);
        heightData[ resY - y - 1, resX - x - 1 ] = 1.0f + z * 5.0f;
      }

    // Route terrain updates through DeformableTerrain so AGX depth offsets and
    // Unity TerrainData stay in sync. Writing TerrainData directly here drifts
    // the terrain height/offset state across play sessions.
    m_terrain.SetHeights( 0, 0, heightData );
    m_terrain.Native.getProperties().setSoilParticleSizeScaling( 1.5f );
  }


  // Update is called once per frame
  void Update()
  {
#if ENABLE_INPUT_SYSTEM

    // If the reset key is pressed.
    if ( m_listenForResetInput && ResetAction != null && ResetAction.triggered )
#else
    if ( m_listenForResetInput && Input.GetKeyDown(ResetTerrainKey) )
#endif
    {
      ResetMeasurements();
    }

    Debug.Assert( m_terrain != null );

    m_massInBucket = (float)m_terrain.Native.getDynamicMass( shovel.Native );
  }
}
