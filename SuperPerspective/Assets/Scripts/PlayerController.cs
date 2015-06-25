﻿using UnityEngine;
using System.Collections;

[RequireComponent(typeof(LineRenderer))]
public class PlayerController : MonoBehaviour
{
	//suppress warnings
	#pragma warning disable 1691,168,219,414

    #region Properties & Variables

	//singleton
	public static PlayerController instance;

    // Movement Parameters
    public float acceleration;
    public float decelleration;
    public float maxSpeed;
	 public float hangMaxSpeed;
    public float gravity;
    public float terminalVelocity;
    public float jump;
    public float jumpMargin;
    private bool _paused;

    // Layer mask used for collision checks
    private int layerMask;  // Not needed?

    // Verticle movement flags
    private bool grounded;
    public bool falling;
    private bool canJump;
    private bool lastInput;
	private bool bounced;
    private float jumpPressedTime;

    // Raycasting Variables
    public int verticalRays = 8;
	float Margin = 0.05f;
    
    private Rect box;
    private float colliderHeight;
    private float colliderWidth;
    private float colliderDepth;

	private CollisionChecker colCheck;

    // Vector used to store new veolicty
    private Vector3 velocity;

	// Vars for Z-locking
	private float zlock = int.MinValue;
	private bool zlockFlag;

	private Animator anim;
	private GameObject model;

	private Crate crate = null;
	private bool pushFlag = false;
	private Vector3 grabAxis = Vector3.zero;

	//direction of player
	private float orientation = 0;

	//Vars for edge grabbing
	private Vector3[] cuboid;
	Edge grabbedEdge = null;
	byte edgeState = 0;//0: not near an edge, 1: close to an edge, 2:hanging
	bool climbing = false;

    #endregion

	//setup singleton
	void Awake(){
		if(instance == null)
			instance = this;
		else if(instance!= this)
			Destroy(this);
	}
	
    // Initialization
	void Start () {

        // Set player as falling 
        // TODO: Since the player often falls through the ground we should cast a ray down and immediately place then on the ground.
        grounded = false;
        falling = true;
        canJump = false;
        lastInput = false;

        // Initialize the layer mask
        layerMask = LayerMask.NameToLayer("normalCollisions");
        
        // Set the starting velocity to the zero vector
        velocity = Vector3.zero;

        // Get the collider dimensions to use for raycasting
        colliderHeight = GetComponent<Collider>().bounds.max.y - GetComponent<Collider>().bounds.min.y;
        colliderWidth = GetComponent<Collider>().bounds.max.x - GetComponent<Collider>().bounds.min.x;
        colliderDepth = GetComponent<Collider>().bounds.max.z - GetComponent<Collider>().bounds.min.z;

		//Register Flip method to the shift event
		//CameraController2.instance.ShiftCompleteEvent += Flip;

		anim = GetComponentInChildren<Animator>();
		model = anim.gameObject;

		//cuboid
		cuboid = new Vector3[2];

		colCheck = new CollisionChecker (GetComponent<Collider> ());

        // Register event handlers
        GameStateManager.instance.GamePausedEvent += OnPauseGame;
	}

    void Update()
    {
        if (!_paused)
        {
            // See if the player is pressing the jump button this frame
            bool input = InputManager.instance.JumpStatus();

            // If this is the first frame the button was pressed then store the current time
            if (input && !lastInput)
            {
                jumpPressedTime = Time.time;
            }
            // If the player didn't press the jump key this frame store time as 0
            else if (!input)
            {
                jumpPressedTime = 0;
            }

            // This only runs while the player is grounded, it allows the player to jump as soon as they land 
            //      even if they pressed the jump button while in midair if they were close enough to the ground
            if ((grounded || edgeState == 2) && Time.time - jumpPressedTime < jumpMargin && crate == null)
            {
					anim.SetTrigger("Jump");
					grounded = false;
					velocity = new Vector3(velocity.x, jump, velocity.z);
					jumpPressedTime = 0;
					ReleaseEdge();
					//update edgeState for animation
					anim.SetInteger("EdgeState", 6);
            }

            lastInput = input;

            anim.SetBool("Falling", falling);
            anim.SetBool("Grounded", grounded);
				
        }
    }

    // Collision detection and velocity calculations are done in the fixed update step
    void FixedUpdate()
    {
        if (!_paused)
        {
				//update climbing variable
				climbing = anim.GetCurrentAnimatorStateInfo(0).IsName("HangUp");
			  
            //update cuboid for edges
				Vector3 halfScale = gameObject.transform.GetChild(3).transform.lossyScale * .5f;
				cuboid[0] = gameObject.transform.GetChild(3).transform.position - halfScale;
				cuboid[1] = gameObject.transform.GetChild(3).transform.position + halfScale;

            if (zlockFlag)
            {
                DoZLock();
                zlockFlag = false;
            }

            // ------------------------------------------------------------------------------------------------------
            // VERTICAL MOVEMENT VELOCITY CALCULATIONS
            // ------------------------------------------------------------------------------------------------------
            if (edgeState != 2 && !climbing)
            {
                // Apply Gravity
                if (!grounded)
                    velocity = new Vector3(velocity.x, Mathf.Max(velocity.y - gravity, -terminalVelocity), velocity.z);

                // Determine if the player is falling
                if (velocity.y < 0f && edgeState == 0)
                    falling = true;
            }
            else
            {
                //if holding on to edge set velocity to 0 and set falling to false
                velocity.y = 0;
                falling = false;
            }

            // ------------------------------------------------------------------------------------------------------
            // X-AXIS MOVEMENT VELOCITY CALCULATIONS
            // ------------------------------------------------------------------------------------------------------
            float newVelocityX = velocity.x;
            //if we're either not on an edge
            if (edgeState != 2 && !climbing)
            {
                float xAxis = InputManager.instance.GetForwardMovement();
                if (xAxis != 0)
                {
                    newVelocityX += acceleration * Mathf.Sign(xAxis);
                    newVelocityX = Mathf.Clamp(newVelocityX, -maxSpeed * Mathf.Abs(xAxis), maxSpeed * Mathf.Abs(xAxis));
                }
                else if (velocity.x != 0)
                {
                    int modifier = velocity.x > 0 ? -1 : 1;
                    newVelocityX += Mathf.Min(decelleration, Mathf.Abs(velocity.x)) * modifier;
                }
            }
				//shimmy
				else if(grabbedEdge!=null && !climbing && grabbedEdge.getOrientation() % 2 == 1){
					//adjust velocity
					float xAxis = InputManager.instance.GetForwardMovement();
					if(xAxis == 0)
						newVelocityX = 0;
					else
						newVelocityX = hangMaxSpeed * Mathf.Sign(xAxis);
					
					//clamp motion
					float newX = gameObject.transform.position.x;
					float edgeX = grabbedEdge.gameObject.transform.position.x;
					float edgeScale = grabbedEdge.gameObject.transform.localScale.x;
					float minBound = edgeX - edgeScale * .5f;
					float maxBound = edgeX + edgeScale * .5f;
					if (!(minBound <= newX && newX <= maxBound))
					{
						newX = Mathf.Clamp(newX, minBound, maxBound);
						newVelocityX = 0;
						Vector3 pos = gameObject.transform.position;
						pos.x = newX;
						gameObject.transform.position = pos;
					}
					//update edgeState for animation
					if(newVelocityX!=0 && anim.GetInteger("EdgeState") < 3){
						anim.SetInteger("EdgeState", 3);
					}
				}
            else
            {
                //if we're latched to a wall which doesn't allow x axis movement don't move along x axis
                newVelocityX = 0;
            }

            velocity.x = newVelocityX;

            // ------------------------------------------------------------------------------------------------------
            // Z-AXIS MOVEMENT VELOCITY CALCULATIONS
            // ------------------------------------------------------------------------------------------------------
            float newVelocityZ = velocity.z;
            //if we're either not on an edge or on an edge which allows z movement
            if (edgeState != 2 && !climbing)
            {
                float zAxis = -InputManager.instance.GetSideMovement();

                if (zAxis != 0)
                {
                    newVelocityZ += acceleration * Mathf.Sign(zAxis);
                    newVelocityZ = Mathf.Clamp(newVelocityZ, -maxSpeed * Mathf.Abs(zAxis), maxSpeed * Mathf.Abs(zAxis));
                }
                else if (velocity.z != 0)
                {
                    int modifier = velocity.z > 0 ? -1 : 1;
                    newVelocityZ += Mathf.Min(decelleration, Mathf.Abs(velocity.z)) * modifier;
                }
            }else if(!climbing && grabbedEdge.getOrientation() % 2 == 0){
					float zAxis = -InputManager.instance.GetSideMovement();
					if(zAxis == 0)
						newVelocityZ = 0f;
					else
						newVelocityZ = hangMaxSpeed * Mathf.Sign(zAxis);
					//clamp position
					float newZ = gameObject.transform.position.z + (newVelocityZ/50f);
					float edgeZ = grabbedEdge.gameObject.transform.position.z;
					float edgeScale = grabbedEdge.gameObject.transform.localScale.z;
					float minBound = edgeZ - edgeScale * .5f;
					float maxBound = edgeZ + edgeScale * .5f;
					if (!(minBound <= newZ && newZ <= maxBound))
					{
						newZ = Mathf.Clamp(newZ, minBound, maxBound);
						newVelocityZ = 0;
						Vector3 pos = gameObject.transform.position;
						pos.z = newZ;
						gameObject.transform.position = pos;
					}
					
					//update edgeState for animation
					if(newVelocityZ!=0 && anim.GetInteger("EdgeState") < 3){
						anim.SetInteger("EdgeState", 3);
					}
				}
            else
            {
                //if we're latched to a wall which doesn't allow z movement
                newVelocityZ = 0;
            }

            velocity.z = newVelocityZ;

            bool walking = edgeState != 2 && (Mathf.Abs(velocity.z) > 0.1 || Mathf.Abs(velocity.x) >= 0.1)/* && grounded*/;
				bool shimmying = edgeState == 2 && ( Mathf.Abs(velocity.z) > 0.1 || Mathf.Abs(velocity.x) >= 0.1 );
            bool running = (Mathf.Abs(velocity.z) >= maxSpeed / 2 || Mathf.Abs(velocity.x) >= maxSpeed / 2);
            anim.SetBool("Walking", walking && !running && crate == null);
            anim.SetBool("Running", running && crate == null);
					
				if (crate == null) {
						anim.SetBool("Pushing", false);
						anim.SetBool("Pulling", false);
				} else {
					anim.SetBool("Pushing", Vector3.Dot(velocity, grabAxis) > 0);
					anim.SetBool("Pulling", Vector3.Dot(velocity, grabAxis) < 0);
				}
				// ------------------------------------------------------------------------------------------------------
            // MANAGE EDGE STATE
            // ------------------------------------------------------------------------------------------------------
				if (walking && crate == null && edgeState < 2 && !climbing)
				{
					orientation = Mathf.Rad2Deg * Mathf.Atan2(-velocity.z, velocity.x) + 90;
				}else if(edgeState >= 2){
					orientation = (-1 - grabbedEdge.getOrientation()) * 90;
				}				
				model.transform.rotation = Quaternion.AngleAxis(orientation, Vector3.up);
			
				// ------------------------------------------------------------------------------------------------------
            // MANAGE EDGE STATE
            // ------------------------------------------------------------------------------------------------------
				int animEdgeState = anim.GetInteger("EdgeState");
				if(animEdgeState < 3)
					anim.SetInteger("EdgeState", edgeState);
				else if(animEdgeState == 3){
						if(Mathf.Abs(velocity.z) < 0.1 && Mathf.Abs(velocity.x) < 0.1)
							anim.SetInteger("EdgeState", edgeState);
				}else if(!anim.GetCurrentAnimatorStateInfo(0).IsName("HangBegin") && !anim.GetCurrentAnimatorStateInfo(0).IsName("HangShimmying"))
					anim.SetInteger("EdgeState", edgeState);
				}
						

            // ------------------------------------------------------------------------------------------------------
            // COLLISION CHECKING
            // ------------------------------------------------------------------------------------------------------
            CheckCollisions();
    }

	public void CheckCollisions(){
		Vector3 trajectory;

		RaycastHit[] hits = colCheck.CheckYCollision (velocity, Margin);

		float close = -1;
		for (int i = 0; i < hits.Length; i++) {
			RaycastHit hitInfo = hits[i];
			if (hitInfo.collider != null)
			{
				if (hitInfo.collider.gameObject.tag == "Intangible") {
					trajectory = velocity.y * Vector3.up;
					CollideWithObject(hitInfo, trajectory);
				} else if (close == -1 || close > hitInfo.distance) {
					close = hitInfo.distance;
					if (velocity.y < 0) {
						grounded = true;
						falling = false;
						canJump = true;
					}
					// Z-lock
					if (hitInfo.collider.gameObject.GetComponent<LevelGeometry>())
						zlock = hitInfo.transform.position.z;
					else
						zlock = int.MinValue;
					trajectory = velocity.y * Vector3.up;
					CollideWithObject(hitInfo, trajectory);
				}
			}
		}
		if (close == -1) {
			grounded = false;
		} else {
			if (!bounced) {
				transform.Translate(Vector3.up * Mathf.Sign(velocity.y) * (close - colliderHeight / 2));
				velocity = new Vector3(velocity.x, 0f, velocity.z);
			} else {
				bounced = false;
			}
		}

		// Third check the player's velocity along the X axis and check for collisions in that direction is non-zero

		// If any rays connected move the player and set grounded to true since we're now on the ground
		pushFlag = false;

		hits = colCheck.CheckXCollision (velocity, Margin);

		close = -1;
		for (int i = 0; i < hits.Length; i++) {
			RaycastHit hitInfo = hits[i];
			if (hitInfo.collider != null)
			{
				if (hitInfo.collider.gameObject.tag == "Intangible") {
					trajectory = velocity.x * Vector3.right;
					CollideWithObject(hitInfo, trajectory);
				} else if (close == -1 || close > hitInfo.distance) {
					close = hitInfo.distance;
					transform.Translate(Vector3.right * Mathf.Sign(velocity.x) * (hitInfo.distance - colliderWidth / 2));
					trajectory = velocity.x * Vector3.right;
					CollideWithObject(hitInfo, trajectory);
				}
			}
		}
		if (close != -1) {
			if (!pushFlag) {
				//transform.Translate(Vector3.right * Mathf.Sign(velocity.x) * (close - colliderWidth / 2));
				velocity = new Vector3(0f, velocity.y, velocity.z);
			}
		}

		
		// Fourth do the same along the Z axis  

		// If any rays connected move the player and set grounded to true since we're now on the ground
		hits = colCheck.CheckZCollision (velocity, Margin);

		close = -1;
		for (int i = 0; i < hits.Length; i++) {
			RaycastHit hitInfo = hits[i];
			if (hitInfo.collider != null)
			{
				if (hitInfo.collider.gameObject.tag == "Intangible") {
					trajectory = velocity.z * Vector3.forward;
					CollideWithObject(hitInfo, trajectory);
				} else if (close == -1 || close > hitInfo.distance) {
					close = hitInfo.distance;
					transform.Translate(Vector3.forward * Mathf.Sign(velocity.z) * (hitInfo.distance - colliderDepth / 2));
					trajectory = velocity.z * Vector3.forward;
					CollideWithObject(hitInfo, trajectory);
				}
			}
		}
		if (close != -1) {
			if (!pushFlag) {
				//transform.Translate(Vector3.forward * Mathf.Sign(velocity.z) * (close - colliderDepth / 2));
				velocity = new Vector3(velocity.x, velocity.y, 0f);
			}
		}

	}

	// LateUpdate is used to actually move the position of the player
	void LateUpdate () {
        if (!_paused)
        {
            if (crate != null)
            {
                //crate.transform.Translate(Vector3.Dot(velocity, grabAxis) * grabAxis * 0.75f * Time.deltaTime);
				Vector3 drag = Vector3.Dot(velocity, grabAxis) * grabAxis * 0.75f;
				crate.SetVelocity(drag.x, drag.z);
                transform.Translate(drag * Time.deltaTime);
            }
            else
            {
                transform.Translate(velocity * Time.deltaTime);
            }
        }
    }

    private void DoZLock() {
		if (crate != null)
			zlock = crate.transform.position.z;
		if (zlock > int.MinValue) {
			Vector3 pos = transform.position;
			pos.z = zlock;
			transform.position = pos;
		}
	}

	public bool Check2DIntersect() {
		// True if any ray hits a collider
		bool connected = false;

		GameObject grnd = GameObject.Find("Ground");
		float gz = grnd.transform.lossyScale.z;

		//reference variables
		float minX 		= GetComponent<Collider>().bounds.min.x + Margin;
		float centerX 	= GetComponent<Collider>().bounds.center.x;
		float maxX 		= GetComponent<Collider>().bounds.max.x - Margin;
		float minY 		= GetComponent<Collider>().bounds.min.y + Margin;
		float centerY 	= GetComponent<Collider>().bounds.center.y;
		float maxY 		= GetComponent<Collider>().bounds.max.y - Margin;
		float centerZ	= GetComponent<Collider>().bounds.center.z - gz/2;

		//array of startpoints
		Vector3[] startPoints = {
			new Vector3(minX, maxY, centerZ),
			new Vector3(maxX, maxY, centerZ),
			new Vector3(minX, minY, centerZ),
			new Vector3(maxX, minY, centerZ),
			new Vector3(centerX, centerY, centerZ)
		};

		//ignore intagible objects
		GameObject[] intangibles = GameObject.FindGameObjectsWithTag("Intangible");
		foreach (GameObject obj in intangibles) {
			obj.layer = 2;
		}

		//check all startpoints
		for(int i = 0; i < startPoints.Length; i++)
			connected = connected || Physics.Raycast(startPoints[i], Vector3.forward) || Physics.Raycast(startPoints[i], -1 * Vector3.forward);

		foreach (GameObject obj in intangibles) {
			obj.layer = 0;
		}

		return connected;
	}

	// Used to check collisions with special objects
	// Make this more object oriented? Collidable interface?
	private void CollideWithObject(RaycastHit hitInfo, Vector3 trajectory) {
		GameObject other = hitInfo.collider.gameObject;
		float colliderDim = 0;
		if (trajectory.normalized == Vector3.up || trajectory.normalized == Vector3.down)
			colliderDim = colliderHeight;
		if (trajectory.normalized == Vector3.right || trajectory.normalized == Vector3.left)
			colliderDim = colliderWidth;
		if (trajectory.normalized == Vector3.forward || trajectory.normalized == Vector3.back)
			colliderDim = colliderDepth;
		// Rune Switch
		if (other.GetComponent<PushSwitch>()) {
			other.GetComponent<PushSwitch>().EnterCollisionWithPlayer();
		}
		// Bounce Pad
		if (trajectory.normalized == Vector3.down && other.GetComponent<BouncePad>()) {
			velocity.y = other.GetComponent<BouncePad>().GetBouncePower();
			other.GetComponent<BouncePad>().Animate();
			anim.SetTrigger("Jump");
			bounced = true;
		}
		// Crate
		if (trajectory.normalized != Vector3.down && trajectory.normalized != Vector3.zero && other.GetComponent<Crate>() && !other.GetComponent<Crate>().IsAxisBlocked(trajectory)) {
			other.GetComponent<Crate>().SetVelocity((trajectory*0.75f).x, (trajectory*0.75f).z);
			pushFlag = true;
			if (crate == null && velocity != Vector3.zero)
				anim.SetBool("Pushing", true);
		} else if (crate == null) {
			anim.SetBool("Pushing", false);
		}
		if (other.GetComponent<PushSwitchOld>() && colliderDim == colliderWidth) {
			transform.Translate(0, 0.1f, 0);
		}
  		//Collision w/ PlayerInteractable
		foreach (Interactable c in other.GetComponents<Interactable>()) {
			c.EnterCollisionWithPlayer ();
		}
	}

	public void Flip() {
		DoZLock();
	}

	public void Grab(Crate crate) {
		this.crate = crate;
		if (crate == null)
			return;
		if (GameStateManager.instance.currentPerspective == PerspectiveType.p2D || Mathf.Abs (crate.transform.position.x - transform.position.x) > Mathf.Abs (crate.transform.position.z - transform.position.z)) {
			grabAxis = Vector3.right * Mathf.Sign(crate.transform.position.x - transform.position.x);
		} else {
			grabAxis = Vector3.forward * Mathf.Sign(crate.transform.position.z - transform.position.z);
		}
	}

	public Vector3[] getCuboid(){
		return cuboid;
	}

	#region EdgeGrabbing
		
	//This can only be called from self
	void ReleaseEdge(){
		if(grabbedEdge!=null)
			grabbedEdge.resetStatus();
		grabbedEdge = null;
		edgeState = 0;
	}

	//note: this is only called from the Edge.cs
	public void UpdateEdgeState(Edge e, byte edgeState){
		UpdateEdgeState(e,edgeState,-1);
	}
	public void UpdateEdgeState(Edge e, byte edgeState, int animState){
		switch(edgeState){
			case 0:
				if(grabbedEdge == e){
					this.edgeState = 0;
					grabbedEdge =null;
				}
				//adjust animation state
				if(animState!= -1)
					anim.SetInteger("EdgeState", animState);
				break;
			case 1:
				this.edgeState = 1;
				grabbedEdge = e;
				break;
			case 2:
				this.edgeState	 = 2;
				//stop moving
				velocity = Vector3.zero;
				//lock y
				Vector3 pos = gameObject.transform.position;
				pos.y = e.gameObject.transform.position.y + (e.gameObject.transform.localScale.y * .5f) - (gameObject.transform.localScale.y * .5f);
				gameObject.transform.position = pos;
				
				break;
		}
	}
	
	#endregion EdgeGrabbing

	public bool is3D(){
		return GameStateManager.instance.currentPerspective== PerspectiveType.p3D;
	}
	
	public float getOrientation(){
		return orientation;
	}
	
	public void Teleport(Vector3 newPos){
		transform.position = newPos;
		gameObject.GetComponent<BoundObject>().updateBounds();
	}

	public Vector3 GetVelocity() {
		return velocity;
	}

	private void OnPauseGame(bool p)
	{
	  _paused = p;
	}
}
