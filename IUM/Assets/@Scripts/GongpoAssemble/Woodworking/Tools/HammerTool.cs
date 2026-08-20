using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class HammerTool : MonoBehaviour
{
    private Rigidbody rb;
    private Grabbable customGrabbable;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        
        customGrabbable = GetComponent<Grabbable>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        ChiselTool chisel = collision.gameObject.GetComponent<ChiselTool>();
        if (chisel != null)
        {
            float hitVelocity = collision.relativeVelocity.magnitude;
            
            // 너무 약한 타격은 무시
            if (hitVelocity > 0.5f)
            {
                chisel.OnHammerHit(hitVelocity);
                
                // 타격 시 망치를 쥔 손에 강한 진동
                if (customGrabbable != null && customGrabbable.IsHeld)
                {
                    UserInput.Instance.SendHapticImpulse(customGrabbable.Holder.Hand, Mathf.Clamp01(hitVelocity / 5f), 0.1f);
                }
            }
        }
    }
}
