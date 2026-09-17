using UnityEngine;

/// <summary>
/// Physics-based first-person movement in the Source/Quake tradition: rigidbody
/// velocity is steered directly with acceleration + friction. There is
/// deliberately no air control — once you leave the ground your horizontal
/// velocity is locked in until you land, so jump arcs and bhop chains are
/// decided entirely by your speed and direction at the moment you left the ground.
///
/// Needs a Rigidbody (gravity off, rotation frozen — set up in Awake) and a
/// CapsuleCollider. Remove any CharacterController. Parent the camera under this
/// object and assign <see cref="cameraTransform"/> (falls back to Camera.main).
///
/// Controls: WASD move, mouse look, Space jump (hold to auto-bhop),
/// Left Ctrl / C crouch, Left Shift to slide (when moving fast on the ground),
/// Q to kick (hit-stop + shake on contact; launches you back off walls / ground).
///
/// The kick's hit check does NOT run on key-press — it fires from
/// <see cref="KickContact"/>, which the kick animation must call via an
/// Animation Event on the frame the foot actually reaches its target. Add that
/// event to the clip or nothing will ever connect (kickTimeout just cancels the
/// attempt quietly if it's missing).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class Player : MonoBehaviour
{
    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float pitchClamp = 85f;
    [SerializeField] private Transform cameraTransform;

    [Header("Ground movement")]
    [Tooltip("Target top speed under player control on the ground.")]
    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float groundAccel = 90f;
    [SerializeField] private float groundFriction = 6f;
    [Tooltip("Speed below which friction is measured against this instead, so you still stop crisply from a walk.")]
    [SerializeField] private float stopSpeed = 2f;

    [Header("Air movement")]
    [Tooltip("Absolute ceiling on horizontal speed. Safety net against runaway slide chains.")]
    [SerializeField] private float hardSpeedCap = 30f;

    [Header("Jump / bunny hop")]
    [SerializeField] private float jumpHeight = 1.1f;
    [SerializeField] private float gravity = 20f;
    [Tooltip("Hold Space to auto-hop the instant you land (classic bhop). Off = tap to jump, with a short buffer + coyote window.")]
    [SerializeField] private bool autoBhop = true;
    [SerializeField] private float jumpBuffer = 0.1f;
    [SerializeField] private float coyoteTime = 0.08f;

    [Header("Crouch / slide")]
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float crouchSpeed = 3f;
    [SerializeField] private float crouchLerp = 12f;
    [Tooltip("Minimum horizontal speed needed to start a slide.")]
    [SerializeField] private float slideMinSpeed = 6f;
    [Tooltip("Instant speed kick when a slide begins.")]
    [SerializeField] private float slideImpulse = 4f;
    [SerializeField] private float slideFriction = 1.4f;
    [Tooltip("How fast you can curve a slide toward where you're looking, in degrees/sec. Magnitude-preserving — you cannot gain speed by steering.")]
    [SerializeField] private float slideSteerSpeed = 130f;
    [Tooltip("Extra push down slopes while sliding (zero effect on flat ground).")]
    [SerializeField] private float slideSlopeAccel = 8f;
    [SerializeField] private float maxSlideTime = 1.5f;

    [Header("Ground check")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float maxSlopeAngle = 50f;
    [SerializeField] private float groundSnapTolerance = 0.2f;

    [Header("Camera feel")]
    [Tooltip("Head-bob rate. Scales with speed on top of this.")]
    [SerializeField] private float bobFrequency = 9f;
    [SerializeField] private float bobVertical = 0.055f;
    [SerializeField] private float bobHorizontal = 0.04f;
    [Tooltip("Horizontal speed at which head-bob reaches full amplitude.")]
    [SerializeField] private float bobFullSpeed = 8f;
    [Tooltip("Metres the view dips per m/s of downward speed on landing.")]
    [SerializeField] private float landDipPerSpeed = 0.018f;
    [SerializeField] private float maxLandDip = 0.28f;
    [Tooltip("Ignore landings softer than this (m/s) so bunny-hops don't jolt.")]
    [SerializeField] private float landDipThreshold = 5f;
    [SerializeField] private float landRecoverTime = 0.32f;
    [Tooltip("Camera roll while strafing, in degrees.")]
    [SerializeField] private float strafeLean = 3.5f;
    [SerializeField] private float leanResponse = 9f;

    [Header("Screen shake")]
    [Tooltip("How quickly a shake dies down (trauma units / sec).")]
    [SerializeField] private float shakeDecay = 1.8f;
    [SerializeField] private float shakeFrequency = 22f;
    [SerializeField] private float shakePosAmount = 0.12f;
    [SerializeField] private float shakeRotAmount = 2.5f;

    [Header("Kick")]
    [SerializeField] private KeyCode kickKey = KeyCode.Q;
    [Tooltip("Animator with the kick trigger. Auto-found in children if left empty.")]
    [SerializeField] private Animator kickAnimator;
    [SerializeField] private string kickTrigger = "Kick";
    [SerializeField] private float kickCooldown = 0.6f;
    [Tooltip("Safety net only: cancels the kick if KickContact() is never called (e.g. the Animation Event is missing).")]
    [SerializeField] private float kickTimeout = 1f;
    [Tooltip("Distance from the player to the centre of the hit sphere.")]
    [SerializeField] private float kickRange = 1.2f;
    [SerializeField] private float kickRadius = 0.5f;
    [Tooltip("Vertical offset of the hit sphere from the player pivot (kick low).")]
    [SerializeField] private float kickHeight = -0.3f;
    [SerializeField] private LayerMask kickMask = ~0;
    [Tooltip("Impulse dealt to dynamic rigidbodies you connect with.")]
    [SerializeField] private float kickForce = 12f;

    [Header("Kick impact")]
    [Tooltip("Time scale held briefly on any connection (hit-stop).")]
    [SerializeField] private float hitStopScale = 0.05f;
    [SerializeField] private float hitStopDuration = 0.08f;
    [Range(0f, 1f)]
    [SerializeField] private float kickShakeTrauma = 0.7f;

    [Header("Kick recoil (off walls / ground)")]
    [SerializeField] private float kickRecoilSpeed = 9f;
    [SerializeField] private float kickRecoilLift = 3f;
    [Tooltip("Seconds movement input is ignored after being launched back.")]
    [SerializeField] private float kickRecoilLock = 0.25f;
    [SerializeField] private float kickRecoilDrag = 1.5f;

    public Vector3 Velocity => rb != null ? rb.velocity : Vector3.zero;
    public float HorizontalSpeed { get { var v = Velocity; v.y = 0f; return v.magnitude; } }
    public bool IsGrounded => grounded;
    public bool IsSliding => sliding;

    private Rigidbody rb;
    private CapsuleCollider capsule;

    private float yaw;
    private float pitch;

    // Input latched in Update, consumed in FixedUpdate.
    private Vector2 moveInput;
    private bool jumpHeld;
    private bool crouchHeld;
    private bool slideHeld;
    private float lastJumpPressed = -99f;
    private float lastGrounded = -99f;

    private bool grounded;
    private Vector3 groundNormal = Vector3.up;

    private bool sliding;
    private float slideTimer;

    private float standHeight;
    private float standCamY;
    private float targetHeight;
    private readonly RaycastHit[] groundHits = new RaycastHit[8];

    // Camera feel.
    private bool wasGrounded;
    private float camHeight;      // base eye height, set by UpdateHeight
    private float bobPhase;
    private float landDip;        // current vertical view offset (<= 0)
    private float landDipVel;
    private float pendingLandSpeed;
    private float currentLean;
    private float shakeTrauma;

    // Kick.
    private float kickReadyTime;
    private float kickStartTime = -99f;
    private bool kickWindingUp;   // kick started, hit not yet resolved
    private float recoilUntil;
    private float hitStopUntil;   // measured in unscaled time
    private bool hitStopActive;
    private readonly Collider[] kickOverlap = new Collider[8];

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // We do our own friction — the collider must not add any of its own.
        capsule.material = new PhysicMaterial("PlayerFrictionless")
        {
            staticFriction = 0f,
            dynamicFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounceCombine = PhysicMaterialCombine.Minimum,
        };

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (kickAnimator == null)
            kickAnimator = GetComponentInChildren<Animator>();

        standHeight = capsule.height;
        targetHeight = standHeight;
        standCamY = cameraTransform != null ? cameraTransform.localPosition.y : capsule.height * 0.4f;
        camHeight = standCamY;

        yaw = transform.eulerAngles.y;
    }

    private bool wantCursorLocked = true;

    private void OnEnable() => wantCursorLocked = true;

    private void OnDisable()
    {
        if (hitStopActive) { Time.timeScale = 1f; hitStopActive = false; } // never leave a hit-stop running
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused) ApplyCursorLock();
    }

    private void ApplyCursorLock()
    {
        Cursor.lockState = wantCursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !wantCursorLocked;
    }

    private void Update()
    {
        // Esc releases the cursor, a click recaptures it. Re-asserted every frame
        // so nothing (alt-tab, the editor, UI) can quietly steal the lock.
        if (Input.GetKeyDown(KeyCode.Escape)) wantCursorLocked = false;
        else if (Input.GetMouseButtonDown(0)) wantCursorLocked = true;
        ApplyCursorLock();

        if (wantCursorLocked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -pitchClamp, pitchClamp);
        }
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        jumpHeld = Input.GetButton("Jump");
        if (Input.GetButtonDown("Jump")) lastJumpPressed = Time.time;
        crouchHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
        slideHeld = Input.GetKey(KeyCode.LeftShift);

        if (Input.GetKeyDown(kickKey) && !kickWindingUp && Time.unscaledTime >= kickReadyTime)
            StartKick();

        // Hold the world in slow-mo for the tail of a hit-stop. Only written on
        // enter/exit so it plays nice with any external pause.
        bool wantHitStop = Time.unscaledTime < hitStopUntil;
        if (wantHitStop != hitStopActive)
        {
            hitStopActive = wantHitStop;
            Time.timeScale = wantHitStop ? hitStopScale : 1f;
        }
    }

    private void StartKick()
    {
        kickWindingUp = true;
        kickStartTime = Time.unscaledTime;
        kickReadyTime = Time.unscaledTime + kickCooldown;
        if (kickAnimator != null && !string.IsNullOrEmpty(kickTrigger))
            kickAnimator.SetTrigger(kickTrigger);
    }

    // Runs in FixedUpdate: pure safety net. The actual hit only happens when
    // KickContact() is called from the kick animation's foot-contact frame.
    private void UpdateKick()
    {
        if (!kickWindingUp) return;
        if (Time.unscaledTime - kickStartTime > kickTimeout)
            kickWindingUp = false; // no contact event ever arrived — give up quietly
    }

    /// Call this from an Animation Event placed on the frame the foot actually
    /// touches something. Nothing (hit-stop, shake, force, recoil) happens before
    /// this fires — a whiff (nothing in range) simply ends the kick silently.
    public void KickContact()
    {
        if (!kickWindingUp) return;
        kickWindingUp = false;

        Vector3 centre = transform.position
                       + transform.forward * kickRange
                       + Vector3.up * kickHeight;

        int n = Physics.OverlapSphereNonAlloc(centre, kickRadius, kickOverlap, kickMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider col = kickOverlap[i];
            if (col == capsule || col.transform.IsChildOf(transform)) continue;

            ResolveKickHit(col, centre);
            return;
        }
    }

    private void ResolveKickHit(Collider col, Vector3 point)
    {
        // Juice: always a hit-stop + a shake on any connection.
        hitStopUntil = Time.unscaledTime + hitStopDuration;
        shakeTrauma = Mathf.Clamp01(shakeTrauma + kickShakeTrauma);

        col.SendMessageUpwards("Kicked", transform.forward * kickForce, SendMessageOptions.DontRequireReceiver);

        Rigidbody other = col.attachedRigidbody;
        bool solid = other == null || other.isKinematic;

        if (!solid)
        {
            // A movable thing — boot it away, no recoil for us.
            other.AddForceAtPosition(transform.forward * kickForce + Vector3.up * (kickForce * 0.15f),
                point, ForceMode.Impulse);
        }
        else
        {
            // A wall or the ground — launch ourselves backwards off it.
            sliding = false;
            grounded = false;
            recoilUntil = Time.time + kickRecoilLock;
            rb.velocity = -transform.forward * kickRecoilSpeed + Vector3.up * kickRecoilLift;
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        GroundCheck();
        UpdateKick();

        Vector3 vel = rb.velocity;

        // Landing impact — record the approach speed for the view to react to.
        if (grounded && !wasGrounded && vel.y < -landDipThreshold)
            pendingLandSpeed = -vel.y;
        wasGrounded = grounded;

        // Knock-back state after being launched off a wall/ground by a kick:
        // no player control, just gravity and a little drag.
        if (Time.time < recoilUntil)
        {
            float drag = Mathf.Exp(-kickRecoilDrag * dt);
            vel.x *= drag;
            vel.z *= drag;
            vel.y -= gravity * dt;
            rb.velocity = vel;
            UpdateHeight(dt);
            return;
        }

        Vector3 wishDir = transform.right * moveInput.x + transform.forward * moveInput.y;
        wishDir = Vector3.ClampMagnitude(wishDir, 1f);

        bool jumpQueued = (autoBhop && jumpHeld) || Time.time - lastJumpPressed <= jumpBuffer;
        bool canJump = grounded || Time.time - lastGrounded <= coyoteTime;
        bool doJump = jumpQueued && canJump && HasHeadroom();

        UpdateSlide(ref vel, wishDir, doJump, dt);

        if (grounded && !doJump)
        {
            ApplyFriction(ref vel, sliding ? slideFriction : groundFriction, dt);

            if (sliding)
            {
                SteerSlide(ref vel, wishDir, dt);
                Vector3 downSlope = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
                vel += downSlope * (slideSlopeAccel * dt); // zero on flat ground
            }
            else
            {
                float target = crouchHeld ? crouchSpeed : moveSpeed;
                Vector3 slopeWish = Vector3.ProjectOnPlane(wishDir, groundNormal);
                if (slopeWish.sqrMagnitude > 1e-4f) slopeWish = slopeWish.normalized * wishDir.magnitude;
                Accelerate(ref vel, slopeWish, target, groundAccel, dt);
            }
        }
        // No air control: horizontal velocity is untouched while airborne — it
        // carries whatever speed and direction you had the instant you left
        // the ground, until you land again.

        if (doJump)
        {
            vel.y = Mathf.Sqrt(2f * gravity * jumpHeight);
            grounded = false;
            lastJumpPressed = -99f;
        }
        else if (grounded && vel.y <= 0.01f)
        {
            vel.y = -2f; // hug the ground / follow it downhill
        }
        else
        {
            vel.y -= gravity * dt;
        }

        // Safety net: never let horizontal speed run away.
        float hSpeed = HorizontalSpeedOf(vel);
        if (hSpeed > hardSpeedCap)
        {
            float k = hardSpeedCap / hSpeed;
            vel.x *= k;
            vel.z *= k;
        }

        rb.velocity = vel;

        UpdateHeight(dt);
    }

    // Curve the slide toward the wish direction without changing its speed.
    private void SteerSlide(ref Vector3 vel, Vector3 wishDir, float dt)
    {
        Vector3 h = new Vector3(vel.x, 0f, vel.z);
        float speed = h.magnitude;
        if (speed < 0.01f || wishDir.sqrMagnitude < 0.01f) return;

        Vector3 dir = Vector3.RotateTowards(h / speed, wishDir.normalized,
            slideSteerSpeed * Mathf.Deg2Rad * dt, 0f);
        vel.x = dir.x * speed;
        vel.z = dir.z * speed;
    }

    // Source-style linear acceleration toward the wish direction.
    private void Accelerate(ref Vector3 vel, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        if (wishSpeed <= 0f) return;
        float current = vel.x * wishDir.x + vel.z * wishDir.z;
        float add = wishSpeed - current;
        if (add <= 0f) return;

        float accelSpeed = Mathf.Min(accel * wishSpeed * dt, add);
        vel.x += accelSpeed * wishDir.x;
        vel.z += accelSpeed * wishDir.z;
    }

    private void ApplyFriction(ref Vector3 vel, float friction, float dt)
    {
        float speed = HorizontalSpeedOf(vel);
        if (speed < 0.01f) { vel.x = 0f; vel.z = 0f; return; }

        float control = speed < stopSpeed ? stopSpeed : speed;
        float drop = control * friction * dt;
        float scale = Mathf.Max(speed - drop, 0f) / speed;
        vel.x *= scale;
        vel.z *= scale;
    }

    private void UpdateSlide(ref Vector3 vel, Vector3 wishDir, bool doJump, float dt)
    {
        if (sliding)
        {
            slideTimer += dt;
            bool tooSlow = HorizontalSpeedOf(vel) < slideMinSpeed * 0.6f;
            if (doJump || !slideHeld || !grounded || slideTimer >= maxSlideTime || tooSlow)
            {
                sliding = false;
                targetHeight = standHeight;
            }
            return;
        }

        bool canStartSlide = grounded && !doJump && slideHeld
                             && HorizontalSpeedOf(vel) >= slideMinSpeed;
        if (!canStartSlide) return;

        sliding = true;
        slideTimer = 0f;
        targetHeight = crouchHeight;

        Vector3 dir = new Vector3(vel.x, 0f, vel.z).normalized;
        if (dir == Vector3.zero) dir = wishDir;
        vel += dir * slideImpulse;
    }

    private void UpdateHeight(float dt)
    {
        float wanted = sliding ? crouchHeight : (crouchHeld && grounded ? crouchHeight : standHeight);
        if (wanted > targetHeight && !HasHeadroom(wanted)) wanted = targetHeight; // blocked, stay low
        targetHeight = wanted;

        float h = Mathf.Lerp(capsule.height, targetHeight, 1f - Mathf.Exp(-crouchLerp * dt));
        float feet = capsule.center.y - capsule.height * 0.5f;
        capsule.height = h;
        capsule.center = new Vector3(capsule.center.x, feet + h * 0.5f, capsule.center.z);

        float t = Mathf.InverseLerp(crouchHeight, standHeight, h);
        camHeight = Mathf.Lerp(standCamY - (standHeight - crouchHeight) * 0.8f, standCamY, t);
    }

    private void LateUpdate()
    {
        if (cameraTransform == null) return;
        float dt = Time.deltaTime;

        // --- head-bob while running (ground only) ---
        float speed01 = grounded && !sliding ? Mathf.Clamp01(HorizontalSpeed / bobFullSpeed) : 0f;
        bobPhase = Mathf.Repeat(bobPhase + dt * bobFrequency * (0.5f + speed01), Mathf.PI * 2f);
        float bobY = Mathf.Sin(bobPhase * 2f) * bobVertical * speed01;
        float bobX = Mathf.Cos(bobPhase) * bobHorizontal * speed01;

        // --- landing impact: a downward kick that springs back to zero ---
        if (pendingLandSpeed > 0f)
        {
            landDip -= Mathf.Min(pendingLandSpeed * landDipPerSpeed, maxLandDip);
            pendingLandSpeed = 0f;
        }
        landDip = Mathf.SmoothDamp(landDip, 0f, ref landDipVel, landRecoverTime);

        // --- lean into strafes ---
        float targetLean = -moveInput.x * strafeLean;
        currentLean = Mathf.Lerp(currentLean, targetLean, 1f - Mathf.Exp(-leanResponse * dt));

        // --- screen shake (trauma^2 falloff; keeps moving during hit-stop) ---
        float uDt = Time.unscaledDeltaTime;
        shakeTrauma = Mathf.Max(0f, shakeTrauma - shakeDecay * uDt);
        float s = shakeTrauma * shakeTrauma;
        Vector3 shakePos = Vector3.zero;
        Vector3 shakeRot = Vector3.zero;
        if (s > 0f)
        {
            float n = Time.unscaledTime * shakeFrequency;
            shakePos = new Vector3(Mathf.PerlinNoise(n, 0.7f) - 0.5f,
                                   Mathf.PerlinNoise(n, 3.3f) - 0.5f, 0f) * (2f * shakePosAmount * s);
            shakeRot = new Vector3(Mathf.PerlinNoise(n, 5.1f) - 0.5f,
                                   Mathf.PerlinNoise(n, 7.9f) - 0.5f,
                                   Mathf.PerlinNoise(n, 9.2f) - 0.5f) * (2f * shakeRotAmount * s);
        }

        cameraTransform.localPosition = new Vector3(bobX, camHeight + bobY + landDip, 0f) + shakePos;
        cameraTransform.localRotation = Quaternion.Euler(
            pitch + bobY * 6f + shakeRot.x, shakeRot.y, currentLean + shakeRot.z);
    }

    private void GroundCheck()
    {
        grounded = false;
        groundNormal = Vector3.up;

        float radius = capsule.radius * 0.9f;
        Vector3 origin = transform.position + capsule.center;
        float distance = capsule.height * 0.5f - capsule.radius + groundSnapTolerance;

        int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, groundHits,
            distance, groundMask, QueryTriggerInteraction.Ignore);

        float closest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == capsule || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.distance >= closest) continue;
            if (Vector3.Angle(hit.normal, Vector3.up) > maxSlopeAngle) continue;

            closest = hit.distance;
            grounded = true;
            groundNormal = hit.normal;
        }

        if (grounded) lastGrounded = Time.time;
    }

    private bool HasHeadroom() => HasHeadroom(standHeight);

    private bool HasHeadroom(float forHeight)
    {
        float radius = capsule.radius * 0.9f;
        float feetY = capsule.center.y - capsule.height * 0.5f;
        Vector3 bottom = transform.position + new Vector3(capsule.center.x, feetY + radius, capsule.center.z);
        float castLen = forHeight - radius * 2f;
        if (castLen <= 0f) return true;

        int count = Physics.SphereCastNonAlloc(bottom, radius, Vector3.up, groundHits,
            castLen, groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == capsule || hit.collider.transform.IsChildOf(transform)) continue;
            return false;
        }
        return true;
    }

    private static float HorizontalSpeedOf(Vector3 v)
    {
        v.y = 0f;
        return v.magnitude;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(
            transform.position + transform.forward * kickRange + Vector3.up * kickHeight,
            kickRadius);
    }
}
