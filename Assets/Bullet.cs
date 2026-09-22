using UnityEngine;

/// <summary>
/// Straight-line projectile. Kills the Player on contact. Destroyed on contact
/// with anything "solid" — a collider with no Rigidbody (static level geometry)
/// or an explicitly kinematic one — so it doesn't sail through walls forever.
/// Passes through other dynamic (non-kinematic) rigidbodies untouched; add
/// handling in ResolveHit if you want it to interact with those too.
///
/// Movement is done with a spherecast each frame rather than physics, so a fast
/// bullet can't tunnel through a thin collider between frames, and it works
/// whether or not the sprite itself has a Collider/Rigidbody.
/// </summary>
public class Bullet : MonoBehaviour
{
    [SerializeField]
    float size;
    [SerializeField] private float speed = 20f;
    [Tooltip("Auto-destroy after this long even if it never hits anything.")]
    [SerializeField] private float lifetime = 6f;
    [SerializeField] private LayerMask hitMask = ~0;
    [Tooltip("Radius of the sweep used to detect hits — a bit of thickness so thin/angled colliders can't be skipped over.")]
    [SerializeField] private float castRadius = 0.15f;

    private void Awake()
    {
        transform.localScale = Vector3.zero;
        // If the sprite has its own collider, make sure it's a trigger so the
        // bullet never physically shoves anything around — hits are resolved
        // by the raycast below, not by physics contacts.
        if (TryGetComponent(out Collider ownCollider))
            ownCollider.isTrigger = true;
    }

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        transform.localScale += new Vector3((size+Mathf.Sin(6*Time.time)*0.25f-transform.localScale.x)/5, (size+Mathf.Sin(6*Time.time)*0.25f-transform.localScale.y)/5, (size+Mathf.Sin(6*Time.time)*0.25f-transform.localScale.z)/5);
        float step = speed * Time.deltaTime;

        if (Physics.SphereCast(transform.position, castRadius, transform.forward, out RaycastHit hit,
                step, hitMask, QueryTriggerInteraction.Ignore))
        {
            transform.position = hit.point;
            ResolveHit(hit.collider);
            return;
        }

        transform.position += transform.forward * step;
    }

    private void ResolveHit(Collider other)
    {
        Player player = other.GetComponentInParent<Player>();
        if (player != null)
        {
            player.Kill();
            //Destroy(gameObject);
            return;
        }

        Rigidbody hitRb = other.attachedRigidbody;
        bool solid = hitRb == null || hitRb.isKinematic;
        if (solid)
            Destroy(gameObject);
    }
}
