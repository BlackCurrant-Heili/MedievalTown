using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class RootMotion : MonoBehaviour
{
    Animator animator;
    float threshold = 0.1f;
    public float forwardSpeed = 1.565f;
    public float backwardSpeed = 1.3f;
    float targetSpeed;
    float currentSpeed;
    public string playerName;
    Rigidbody rig;

    // Start is called before the first frame update
    void Start()
    {
        animator = GetComponent<Animator>();
        rig = GetComponent<Rigidbody>();
        Debug.Log(animator.humanScale);
        animator.SetFloat("ScalarFactor", 1/animator.humanScale);
    }
    
    //OnAnimatorMove()与管线
    public void OnAnimatorMove()
    {
        Move();
    }

    void Move()
    {
        currentSpeed = Mathf.Lerp(targetSpeed, currentSpeed, 0.9f);
        animator.SetFloat("Speed", currentSpeed);
        if(Input.GetKey(KeyCode.LeftShift)&Input.GetKey(KeyCode.W))
        {
            animator.SetBool("run", true);
        }
        else
        {
            animator.SetBool("run", false);
        }
        Vector3 vector3 = new Vector3(animator.velocity.x, rig.linearVelocity.y, animator.velocity.z);
        rig.linearVelocity = vector3;
    }

    public void PlayerMove(InputAction.CallbackContext callbackContext)
    {
        Vector2 movement = callbackContext.ReadValue<Vector2>();
        targetSpeed = movement.y > 0 ? forwardSpeed * movement.y : backwardSpeed * movement.y;
    }
}

