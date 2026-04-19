using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraControll : MonoBehaviour
{
    [SerializeField] Transform target;              // 要跟随的目标
    [SerializeField] Vector3 offset;                // 和目标偏移量
    [SerializeField] float transitionSpeed = 5f;    // 过渡速度

    [Header("Rotation Settings")]
    [SerializeField] float rotateSpeed = 300f;      // 旋转灵敏度
    [SerializeField] float minVerticalAngle = -20f; // 垂直旋转最小角度
    [SerializeField] float maxVerticalAngle = 80f;  // 垂直旋转最大角度

    private float _currentHorizontalAngle = 0f;     // 当前水平旋转角度
    private float _currentVerticalAngle = 30f;      // 当前垂直旋转角度（默认俯角30度）
     
    private void LateUpdate()                       // 玩家在update里移动,相机在late update里再跟随
    {
        if (target == null) return;
        
        if (Input.GetMouseButton(1))
        {
            RotateCameraWithMouse();                // 右键按住时旋转摄像机
        }
        
        UpdateCameraPosition();                     // 更新摄像机位置和朝向
    }

    private void RotateCameraWithMouse()            // 鼠标控制旋转逻辑
    {
        float mouseX = Input.GetAxis("Mouse X") * rotateSpeed * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * rotateSpeed * Time.deltaTime;
        _currentHorizontalAngle += mouseX;
        _currentVerticalAngle -= mouseY;            // 鼠标Y轴和摄像机垂直旋转方向相反
        _currentVerticalAngle = Mathf.Clamp(_currentVerticalAngle, minVerticalAngle, maxVerticalAngle);
    }

    
    private void UpdateCameraPosition()             // 更新摄像机位置和朝向
    {
        
        Quaternion rotation = Quaternion.Euler(_currentVerticalAngle, _currentHorizontalAngle, 0);  // 1. 计算旋转后的偏移方向
        Vector3 rotatedOffset = rotation * offset;
        
        Vector3 targetPosition = target.position + rotatedOffset;  // 2. 平滑移动到目标位置
        transform.position = Vector3.Lerp(transform.position, targetPosition, transitionSpeed * Time.deltaTime);
       
        transform.LookAt(target.position);
    }
  
}
