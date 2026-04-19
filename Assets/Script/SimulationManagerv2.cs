// using UnityEngine;

// public class SimulationManagerV2 : MonoBehaviour
// {
//     [Header("系统引用")]
//     public SPH_Solver fluidSystem;
//     public GrassRendererRT_Terrain grassSystem;

//     [Header("交互开关")]
//     public bool fluidAffectGrass = true;  // 流体推动草
//     public bool grassAffectFluid = true;  // 草阻挡流体
//     void Start()
//     {
//         // 自动查找组件
//         if (fluidSystem == null) fluidSystem = FindFirstObjectByType<SPH_Solver>();
//         if (grassSystem == null) grassSystem = FindFirstObjectByType<GrassRendererRT_Terrain>();

//         if (fluidSystem == null || grassSystem == null) {
//             Debug.LogError("SimulationManager: 无法找到流体或草系统！");
//             enabled = false;
//             return;
//         }
//         SetupInteraction();
//     }

//     void SetupInteraction()
//     {
//         // 双向绑定Buffer引用
//         if (fluidAffectGrass) {
//             grassSystem.velocityFieldRTHandle = fluidSystem.velocityFieldRTHandle;
//             grassSystem.sphCellSize = fluidSystem.CellSize;
//             grassSystem.sphDimensions = fluidSystem.Dimensions;
//             grassSystem.sphResolution = fluidSystem.Resolution;
//             // Debug.Log($"[SimulationManager] 流体参数传递: CellSize={grassSystem.sphCellSize}, Dimensions={grassSystem.sphDimensions}");
//         }
//     }

//     void OnDestroy()
//     {
//     }
// }