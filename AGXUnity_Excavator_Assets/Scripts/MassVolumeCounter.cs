using UnityEngine;
using AGXUnity;
using UnityEngine.UI;
using System.Linq;

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
  Terrain m_unityTerrain;

  float m_excavatedMass = 0;
  float m_massInBucket = 0;
  float m_previousMassInBucket = 0;

  Text m_infoText;

  // The current scene doesn't provide the particle/contact sensor that the
  // original excavated-volume pipeline depended on. Keep the excavated output
  // by accumulating positive changes in bucket load across the episode.
  public float ExcavatedMass => m_excavatedMass;
  public float MassInBucket => m_massInBucket;


  protected override bool Initialize()
  {
    Debug.Assert( m_terrain );
    m_unityTerrain = m_terrain.GetComponent<Terrain>();

#if ENABLE_INPUT_SYSTEM
    if ( m_listenForResetInput ) {
      ResetAction = new InputAction( "Reset", binding: "<Keyboard>/r" );
      ResetAction.Enable();
    }
#endif

    var texts = GetComponentsInChildren<Text>();
    m_infoText = texts.FirstOrDefault( t => t.name == "Information" );

    // Initialize the heights of the terrain
    ComputeTerrainHeights();

    return base.Initialize();

  }
  public TerrainData TerrainData { get { return m_unityTerrain?.terrainData; } }

  public void ResetMeasurements( bool resetTerrain = true )
  {
    m_excavatedMass = 0;
    m_massInBucket = 0;
    m_previousMassInBucket = 0;

    if ( resetTerrain && m_terrain != null )
      ComputeTerrainHeights();
  }


  /// <summary>
  /// Reset the terrain.
  /// Compute a new height for the terrain given some function
  /// </summary>
  void ComputeTerrainHeights()
  {
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

    // Overwrite the full terrain through the DeformableTerrain API so Unity
    // TerrainData and AGX depth offsets stay aligned. Calling ResetHeights()
    // here replays the internal terrain transform offset and makes the terrain
    // climb between play sessions in the editor.
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

    if ( m_terrain == null || shovel == null ) {
      m_excavatedMass = 0.0f;
      m_massInBucket = 0.0f;
      m_previousMassInBucket = 0.0f;
      if ( m_infoText != null )
        m_infoText.text = "Mass in bucket: \t\t0.0 kg";
      return;
    }

    m_massInBucket = (float)m_terrain.Native.getDynamicMass( shovel.Native );
    m_excavatedMass += Mathf.Max( 0.0f, m_massInBucket - m_previousMassInBucket );
    m_previousMassInBucket = m_massInBucket;

    string info = string.Format( "Mass in bucket: \t\t{0:f} kg\n", m_massInBucket );
    info += string.Format( "Excavated mass: \t{0:f} kg\n", m_excavatedMass );

    if ( m_infoText != null )
      m_infoText.text = info;
  }
}
