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
}

