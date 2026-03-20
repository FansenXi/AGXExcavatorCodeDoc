using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AGXUnity;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ResetTerrain : MonoBehaviour
{
  [SerializeField]
  private bool m_listenForResetInput = false;

#if ENABLE_INPUT_SYSTEM
  private InputAction ResetAction;
#else
  public KeyCode ResetTerrainKey = KeyCode.R;
#endif

  // Start is called before the first frame update
  void Start()
  {
    if ( m_listenForResetInput && HasCentralizedResetPath() ) {
      m_listenForResetInput = false;
      Debug.Log( "ResetTerrain: standalone reset input disabled because SceneResetService/EpisodeManager is present.", this );
    }

#if ENABLE_INPUT_SYSTEM
    if ( m_listenForResetInput ) {
      ResetAction = new InputAction("Reset", binding: "<Keyboard>/r");
      ResetAction.Enable();
    }
#endif

  }

  public void ResetTerrainHeights()
  {
    var terrain = GetComponent<AGXUnity.Model.DeformableTerrain>();
    if ( terrain != null )
      terrain.ResetHeights();
  }

  // Update is called once per frame
  void Update()
  {
#if ENABLE_INPUT_SYSTEM
    if ( m_listenForResetInput && ResetAction != null && ResetAction.triggered )
#else
    if ( m_listenForResetInput && Input.GetKeyDown(ResetTerrainKey) )
#endif
    {
      ResetTerrainHeights();
    }
  }

  private static bool HasCentralizedResetPath()
  {
    return FindObjectOfType<AGXUnity_Excavator.Scripts.Experiment.SceneResetService>() != null ||
           FindObjectOfType<AGXUnity_Excavator.Scripts.Experiment.EpisodeManager>() != null;
  }
}

