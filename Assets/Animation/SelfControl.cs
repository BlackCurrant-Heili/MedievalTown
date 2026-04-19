using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SelfControl : MonoBehaviour
{
    Animator animator;
    float threshold = 0.1f;
    public float forwardSpeed = 2.0f;
    public float backwardSpeed = 1.5f;
    float targetSpeed;
    float currentSpeed;
    Vector3 movement;

    // Start is called before the first frame update
    void Start()
    {
        animator = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        Move();
    }

    void Move()
    {
        currentSpeed = Mathf.Lerp(targetSpeed, currentSpeed, 0.9f);
        movement = new Vector3(currentSpeed * Time.deltaTime, 0, 0);
        transform.position += movement;
        animator.SetFloat("Speed", currentSpeed);
    }

    public void PlayerMove(InputAction.CallbackContext callbackContext)
    {
        Vector2 movement = callbackContext.ReadValue<Vector2>();
        targetSpeed = movement.y > 0 ? forwardSpeed * movement.y : backwardSpeed * movement.y;
    }
}

