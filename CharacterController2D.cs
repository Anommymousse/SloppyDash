using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DarkTonic.MasterAudio;
using DarkTonic.PoolBoss;
using Unity.Mathematics;
//using UnityEditor.Analytics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;


//Player design
//Jump	- 3 x Height
//		- 6 frames to full speed
//		- 3 frame to full stop, except air full stop immediately if no direction?
//		Particle dust effect 
//		screen shake on land (MMFeedbacks)
//		squish on take off and land
//		rumble on land
//		Jump extra - if jump pressed just before land frame, jump on land
//		36f to fall down 
//		max fall speed for control
//
//Dash	- Directional dash, on grounded can be used again
//		- 4 Frame pause
//		- Trail of shadows
//		- con trail
//		- smol screen shake
//		- 4 bodys worth
//		- SFX
//
//Coyote time - present needs tweaked to 4 frames
//
//Player crouch - removed and replaced with button to build lower brick with look down anim...
//
//Improve animations...

[System.Serializable]
public class HitRoofEvent : UnityEvent<Vector3>
{
}

[System.Serializable]
public class JumpStarted : UnityEvent<Vector3>
{
}


public class CharacterController2D : MonoBehaviour
{
	//
	//Events Generated
	HitRoofEvent _hitRoofEvent = new HitRoofEvent();
	JumpStarted _jumpStartedEvent = new JumpStarted();
	public HitRoofEvent GetRoofHitEvent => _hitRoofEvent;
	public JumpStarted GetJumpStartedEvent => _jumpStartedEvent;
	[Header("Events")]
	[Space]
	public UnityEvent OnLandEvent;
	
	//
	//Events listened to, Dash(player), Land(self), buildabrick
	//
	bool isDashActive = false;

	//Dash power controls
		enum DashStage
		{
			Starting,
			Boost,
			Zoom,
			ControlledZoom,
			Stopping,
			Landing
		}
		DashStage _dashStage;
		//float dashControlDistanceStart = 2.5f;
		float dashMaxDistanceAllowed = 3.5f;
		float dashMaxDistanceExtendedTime = 3.5f;
		float dashSpeed = 25.0f;
		float dashStartChargeTime = 0.15f;
		float _dashTimeTotal = 0.0f;
		float _dashTimeStartDashSound;
		float _dashTimerOffset = 0.2f;
		bool _dashDashSoundStarted;
		Vector2 _dashDirection;
		Vector2 _dashStartPosition;
		//Dash visual fx
		int unitDistanceLastFrame;
		bool xactive;
		bool yactive;
		Vector3 _lastPlayerPosition;
		float _timeThePositionStalled;
		bool _currentlyStalled;
		
		

	//Jump
	[SerializeField] private float m_JumpSpeed = 10f;							// Amount of force added when the player jumps.
	
	//Brick build attempt this frame
	float _timeLastBrickBuildAttemptMade;

	//No longer used.
	//[Range(0, 1)] [SerializeField] private float m_CrouchSpeed = .36f;			// Amount of maxSpeed applied to crouching movement. 1 = 100%
	
	//Detectors
	[SerializeField] private LayerMask m_WhatIsGround;							// A mask determining what is ground to the character
	//[SerializeField] private LayerMask m_playerAndBricksOnly;
	//[SerializeField] private Transform m_GroundCheck;							// A position marking where to check if the player is grounded.
	[SerializeField] Transform m_GroundCheckMiddle;
	[SerializeField] private Transform m_GroundCheckLeft;							// A position marking where to check if the player is grounded.
	[SerializeField] private Transform m_GroundCheckRight;							// A position marking where to check if the player is grounded.
	[SerializeField] private Transform m_CeilingWallCheckLeft;							// A position marking where to check if the player is grounded.
	[SerializeField] private Transform m_CeilingWallCheckRight;							// A position marking where to check if the player is grounded.
	[SerializeField] private Transform m_CeilingMiddleCheck;							// A position marking where to check for ceilings

	string rbodydata;
	//Gravity
	[SerializeField] float timeAccured;
	[SerializeField] float jumpStartTime;
	[SerializeField] float max_fallSpeed = -5.0f;
	float _coyoteJumpTimer = 0.0f;
	float _coyoteJumpAllowance = 0.18f;
	//float _coyoteDisabledTimer = 0.18f;
	
	//Land
	bool _lastFrameGrounded;
	bool _landedThisFrame;
	
	//Powers
	static bool _isSuperJumpActive;
	//float superJumpMultiplier = 1.5f;
	public static void SetSuperJump(bool newState) => _isSuperJumpActive = newState;

	/*
	static bool _isGravityReverseActive;
	public static void SetGravityReverse(bool newState) => _isGravityReverseActive = newState;
	public static bool GetGravityReverse() => _isGravityReverseActive;*/

	//float _wallcheckAdjust_x = 0.0f;
	static bool m_Grounded;            // Whether or not the player is grounded.

	public static bool IsGrounded => m_Grounded;
	
	bool _againstLeftWall;
	bool _againstRightWall;
	bool _againstRoof;
	
	//[SerializeField] float rgb_vel_y = 0.0f;
	private Rigidbody2D m_Rigidbody2D;
	//private bool m_FacingRight = true;  // For determining which way the player is currently facing.

	public Rigidbody2D GetRigidbody() => m_Rigidbody2D;
	
	Collider2D[] Collider2DResults;
	
	SpriteRenderer _playerRenderer;
	


	[System.Serializable]
	public class BoolEvent : UnityEvent<bool> { }

	public BoolEvent OnCrouchEvent;
	//private bool m_wasCrouching = false;
	Animator _playerAnimator;
	static readonly int Walking = Animator.StringToHash("Walking");
	static readonly int Crouching = Animator.StringToHash("Crouching");
	static readonly int Grounded = Animator.StringToHash("Grounded");
	[SerializeField] float _gravitySpeed=100.0f;
	

	void OnEnable()
	{
		OnLandEvent.AddListener(LandEvent);
	}

	void LandEvent()
	{
		if(PoolBoss.IsReady)
			PoolBoss.SpawnInPool("VfxLand", transform.position,Quaternion.identity);
		Player._landFeedbacks?.PlayFeedbacks();
	}

	private void Awake()
	{
		m_Rigidbody2D = GetComponent<Rigidbody2D>();
		_againstLeftWall = false;
		_againstRightWall = false;
		if (OnLandEvent == null)
			OnLandEvent = new UnityEvent();

		if (OnCrouchEvent == null)
			OnCrouchEvent = new BoolEvent();

		_playerRenderer = gameObject.GetComponent<SpriteRenderer>();
		_playerAnimator = GetComponent<Animator>();
		
		Collider2DResults = new Collider2D[4];
		var thing = transform.root;
		
		Player.GetPlayerDashEvent.AddListener(DashEventTriggered);
		Player.GetPlayerBuildEvent.AddListener(BuildABrickSoStopFalling);
		_dashStage = DashStage.Starting;

		//_brickMapReference = thing.GetComponentInChildren<BrickMap>();
	}

	void BuildABrickSoStopFalling(Vector3 arg0, float arg1)
	{
		_timeLastBrickBuildAttemptMade = Time.time;
	}


	void UpdateGrounded()
	{
		if (m_Grounded == true)
			_coyoteJumpTimer = Time.time;
		
		m_Grounded = false;
		Vector2 groundHit = m_GroundCheckRight.position;
		groundHit.y += 0.1f;
		var raycastHit2DMiddle =  Physics2D.RaycastAll(m_GroundCheckMiddle.position, Vector2.down, 0.2f,m_WhatIsGround);
		var raycastHit2D = Physics2D.RaycastAll(m_GroundCheckLeft.position, Vector2.down, 0.2f,m_WhatIsGround);
		var raycastHit2DRight =  Physics2D.RaycastAll(m_GroundCheckRight.position, Vector2.down, 0.2f,m_WhatIsGround);

		if ((raycastHit2DMiddle.Length > 0)) m_Grounded= true;
		
		//Force player to height of ground... if not jumping
		if (m_Rigidbody2D.velocity.y <= 0)
		{
			//If we hit something
			if (raycastHit2DMiddle.Length > 0)
			{
				Vector2 catheight = Vector2.up *0.01f;//* 0.05f;
				var xvel = m_Rigidbody2D.velocity;
				var adjPoint = raycastHit2DMiddle[0].point + catheight;
				//var currentposition = m_Rigidbody2D.position;
				//var positionDiff = raycastHit2DMiddle[0].point - currentposition;
				//currentposition.y += raycastHit2DMiddle.Length + 0.05f;
				m_Rigidbody2D.MovePosition(adjPoint);
			}
		}
		

		//var raycastHit2D = Physics2D.RaycastAll(m_GroundCheckLeft.position, Vector2.down, 0.2f);
/*		foreach(var ray in raycastHit2D)
			Debug.Log($"<color=green> rayhits {ray.collider.name} </color>");
		//var raycastHit2DRight = Physics2D.RaycastAll(m_GroundCheckRight.position, Vector2.down, 0.2f);
		foreach (var ray in raycastHit2DRight)
			Debug.Log($"<color=green> rayhits right {ray.collider.name}  </color>");*/

		//if ((raycastHit2D.Length >0 ) || (raycastHit2DRight.Length>0))
		//	m_Grounded = true;
	}
	
	bool TestWallPoint(Vector2 position, Vector2 direction, float dist)
	{
		bool hasHitWall = false;
		var raycastHit2D = Physics2D.RaycastAll(position, direction, dist,m_WhatIsGround);
		if(raycastHit2D.Length > 0)
		{
			foreach (var VARIABLE in raycastHit2D)
			{
				Debug.Log($" WallTest {VARIABLE.collider.name}");	
			}

			return true;
		}
		return hasHitWall;
	}
	
	bool HandleRoofPoint(Vector2 position, Vector2 direction, float dist)
	{
		bool hasHitWall = false;
		var raycastHit2D = Physics2D.RaycastAll(position, direction, dist,m_WhatIsGround);
		if(raycastHit2D.Length > 0)
		{
			_hitRoofEvent.Invoke(m_CeilingMiddleCheck.position);
			//_brickMapReference.AttemptToBreak(position,Vector2.up*0.1f);
			return true;
		}
		return hasHitWall;
	}

	//Assumes same x pos for the 2 left, 2 right points
	void UpdateWallCollisions()
	{
		_againstLeftWall = false;
		_againstRightWall = false;
		_againstRoof = false;
		
		if (TestWallPoint(m_CeilingWallCheckLeft.position, Vector2.left, 0.1f))
		{
			_againstLeftWall = true;
		}
		if (TestWallPoint(m_GroundCheckLeft.position, Vector2.left, 0.2f))
		{
			_againstLeftWall = true;
		}

		if (TestWallPoint(m_CeilingWallCheckRight.position, Vector2.right, 0.1f))
		{
			_againstRightWall = true;
		}

		if (TestWallPoint(m_GroundCheckRight.position, Vector2.right, 0.2f))
		{
			_againstRightWall = true; 
		}

		if (HandleRoofPoint(m_CeilingMiddleCheck.position, Vector2.up, 0.1f))
		{
			_againstRoof = true;
			m_Rigidbody2D.velocity = Vector2.zero;
		}
	}

	public void Teleport(Vector3 newlocation)
	{
		m_Rigidbody2D.velocity = Vector2.zero;
		m_Rigidbody2D.position = newlocation;
	}

#if UNITY_EDITOR	
	[ContextMenu("SaveOut")]
	void SaveDataOutToDiskAsText()
	{
		string filename = "LogOfRigidBody";


		string path = Application.streamingAssetsPath + "/" + filename + ".txt";
		//string path = Application.persistentDataPath + "/" + filename + ".txt";


        
		if (!File.Exists(path)) {
			File.WriteAllText(path," ");
		}
		File.WriteAllText(path,"Tileset: 0 \n");
		File.AppendAllText(path,rbodydata);
	}
#endif  

	
	//FIXED UPDATE!
	public void Move(float move, bool crouch, bool jump)
	{
		bool wasGrounded = m_Grounded;
		
		UpdateWallCollisions();
		
		if (!isDashActive||_dashStage == DashStage.Landing)
		{
			UpdateGrounded();
			
			if ((m_Grounded) && (!wasGrounded))
				OnLandEvent.Invoke();

			//Sprite flip
			FlipBasedOnMove(move);
			// If the player can jump and presses jump...
			HandleJump(jump);

			HandleAnimations(move, crouch, jump);
		}

		//Move is already scaled by time.deltatime
		if (!isDashActive||_dashStage == DashStage.Landing)
			HandleLeftRight(move);

		//Dash
		if (isDashActive)
			FlipBasedOnMove(Player.GetDashDirection.x);
		HandleDash();
		

	}

	void FlipBasedOnMove(float move)
	{
		if (move < 0)
			_playerRenderer.flipX = true;
		if(move >0)
			_playerRenderer.flipX = false;
	}

	void DashEventTriggered(float timeTriggered)
	{
		isDashActive = true;
		unitDistanceLastFrame = -1;
	}
	
//		- 4 Frame pause
//		- Trail of shadows
//		- con trail
//		- smol screen shake
//		- 4 bodys worth
//		- SFX



	//Distance based dash
	void HandleDash()
	{
		if (isDashActive == false) return;
			
		_dashTimeTotal += Time.deltaTime;

		if (_dashStage == DashStage.Starting)
		{
			MasterAudio.PlaySound("DashChargeUp");
			Vector3 adjposition = transform.position;
			adjposition.y += 0.5f;
			PoolBoss.SpawnInPool("VfxDashGatherPower", adjposition, Quaternion.identity);
			_dashTimeStartDashSound = _dashTimeTotal + _dashTimerOffset;
			_dashDashSoundStarted = false;
			_dashStage = DashStage.Boost;
			xactive = true;
			yactive = true;
			_lastPlayerPosition = gameObject.transform.position;
			_currentlyStalled = false;
			
		}

		if (_dashStage == DashStage.Boost)
		{
			if(_dashDashSoundStarted==false)
			{
				if (_dashTimeStartDashSound < _dashTimeTotal)
				{
					_dashDashSoundStarted = true;
					MasterAudio.PlaySound("DashExecuteTweaked");
				}
			}
			
			Debug.Log("Dash Boost!");
			if (_dashTimeTotal < dashStartChargeTime)
			{
				
				m_Rigidbody2D.velocity = Vector2.zero;
				return;
			}
			
			//Trigger boost stage
			_dashStage = DashStage.Zoom;
			_dashDirection = Player.GetDashDirection;
			if (_dashDirection.y < 0) _dashDirection.y = 0;
			_dashStartPosition = transform.position;

			Quaternion rotationInZ = ConvertDirectionToRotationInZ(_dashDirection);
			PoolBoss.SpawnInPool("VfxDash", transform.position,rotationInZ);
		}

		if (_dashStage == DashStage.Zoom)
		{
			Debug.Log($"Dash Zoom! Direction {_dashDirection} Spd{dashSpeed}");
			Vector2 newVelocity;

			float checkForNoDirection = Mathf.Abs(_dashDirection.x) + Mathf.Abs(_dashDirection.y);
			if (checkForNoDirection < 0.01f)
			{
				if(_playerRenderer.flipX==true)
					newVelocity = Vector2.left *dashSpeed;
				else
					newVelocity = Vector2.right *dashSpeed;
			}
			else
				newVelocity = _dashDirection * dashSpeed;
			m_Rigidbody2D.velocity = newVelocity;
			
			Vector2 position = m_Rigidbody2D.position;
			position += m_Rigidbody2D.velocity * Time.fixedDeltaTime;
			m_Rigidbody2D.MovePosition(position);
			
			//Generate shadow
			
			//Generate con trails
			
			//Check if we hit a wall
			UpdateWallCollisions();
			DashWallHitAdjustDirection();

			//Distance travelled or hit something.
			float distanceSoFar = Vector2.Distance(transform.position, _dashStartPosition);

			DisplayGhosts(distanceSoFar);

			int multiplyFactor = 0;
			if (_isSuperJumpActive)
				multiplyFactor = 1;
			
			if ((dashMaxDistanceAllowed + dashMaxDistanceExtendedTime*multiplyFactor ) < distanceSoFar)
				_dashStage = DashStage.ControlledZoom;
			
			return;
		}
		
		if (_dashStage == DashStage.ControlledZoom)
		{
			Vector2 newVelocity;

			_dashDirection = Player.GetDashDirection;
			if (_dashDirection.y < 0) _dashDirection.y = 0;
			
			float checkForNoDirection = Mathf.Abs(_dashDirection.x) + Mathf.Abs(_dashDirection.y);
			if (checkForNoDirection < 0.01f)
			{
				if(_playerRenderer.flipX==true)
					newVelocity = Vector2.left *dashSpeed;
				else
					newVelocity = Vector2.right *dashSpeed;
			}
			else
				newVelocity = _dashDirection * dashSpeed;
			m_Rigidbody2D.velocity = newVelocity;
			
			Vector2 position = m_Rigidbody2D.position;
			position += m_Rigidbody2D.velocity * Time.fixedDeltaTime;
			m_Rigidbody2D.MovePosition(position);
			
			//Check if we hit a wall
			UpdateWallCollisions();
			DashWallHitAdjustDirection();
			
			//Distance travelled or hit something.
			float distanceSoFar = Vector2.Distance(transform.position, _dashStartPosition);

			DisplayGhosts(distanceSoFar);

			if (dashMaxDistanceAllowed < distanceSoFar)
				_dashStage = DashStage.Stopping;

			return;
		}

		
		if (_dashStage == DashStage.Stopping)
		{
			Debug.Log("Dash Stop!");
			m_Rigidbody2D.velocity = Vector2.zero;
			_dashStage = DashStage.Landing;
			return;
		}

		if (_dashStage == DashStage.Landing)
		{
			if (m_Grounded)
			{
				Player.PlayerDashReset();
				DashReset();
			}
		}
	}

	void DashReset()
	{
		isDashActive = false;
		_dashDashSoundStarted = false;
		_dashTimeTotal = 0.0f;
		_dashStage = DashStage.Starting;
	}

	void DashWallHitAdjustDirection()
	{
		if((_dashStage == DashStage.Zoom)||(_dashStage == DashStage.ControlledZoom))
			if (_lastPlayerPosition == gameObject.transform.position)
			{
				if (_currentlyStalled is false)
				{
					_timeThePositionStalled=Time.time;
					_currentlyStalled = true;
				}
			}
			else
			{
				_currentlyStalled = false;
			}
		else
		{
			_currentlyStalled = false;
		}
		
		if (xactive)
		{
			if ((_againstLeftWall) || (_againstRightWall))
			{
				xactive = false;
				_dashDirection.x = 0;
				_dashDirection.y = 1.0f;
			}
		}

		if (yactive)
		{
			if ((_againstRoof))
			{
				yactive = false;
				_dashDirection.y = 0.0f;
				if (_playerRenderer.flipX == true)
					_dashDirection.x = -1.0f;
				else
					_dashDirection.x = 1.0f;
			}
		}

		if((_dashStage == DashStage.Zoom)||(_dashStage == DashStage.ControlledZoom))
			if (_currentlyStalled)
			{
				if (Time.time > (_timeThePositionStalled + .25f))
				{
					_dashStage = DashStage.Stopping;	
				}
			}

		if ((xactive == false) && (yactive == false))
		{
			_dashStage = DashStage.Stopping;
		}
		
		_lastPlayerPosition = gameObject.transform.position;
	}

	//int stagetest = 0;
	Quaternion ConvertDirectionToRotationInZ(Vector2 dashDirection)
	{
		Quaternion rv;//= Quaternion.identity;
		Vector2 newdirection;
		float checkForNoDirection = Mathf.Abs(_dashDirection.x) + Mathf.Abs(_dashDirection.y);
		if (checkForNoDirection < 0.01f)
		{
			if(_playerRenderer.flipX==true)
				newdirection = Vector2.left *dashSpeed;
			else
				newdirection = Vector2.right *dashSpeed;
		}
		else
			newdirection = _dashDirection * dashSpeed;

		
		float zangle = Mathf.Atan2(-newdirection.y, newdirection.x);
		zangle *= -Mathf.Rad2Deg;
		zangle += 90.0f;
		rv = Quaternion.Euler(0, 0, zangle);
		Debug.Log($" angle set = {zangle}");
		return rv;
		
		/*if (stagetest == 0)
		{
			rv = Quaternion.identity;
			stagetest = 1;
			return rv;
		}

		if (stagetest == 1)
		{
			rv = Quaternion.Euler(0, 0, zangle);
			stagetest = 2;
			return rv;
		}
		if (stagetest == 2)
		{
			rv = Quaternion.Euler(0, 0, 180);
			stagetest = 3;
			return rv;
		}
		if (stagetest == 3)
		{
			rv = Quaternion.Euler(0, 0, -90);
			stagetest = 0;
			return rv;
		}

		return rv;*/
	}

	void DisplayGhosts(float distanceSoFar)
	{
		float howmanyghosts = 6.0f;
		
		float threshold = dashMaxDistanceAllowed / howmanyghosts;

		int distanceInUnits =  Mathf.FloorToInt(distanceSoFar / threshold);

		if (distanceInUnits > unitDistanceLastFrame)
		{
			var thing = PoolBoss.SpawnInPool("VfxShadowCats", transform.position, Quaternion.identity);
			if (thing != null)
			{
				var spriteRenderer = thing.gameObject.GetComponent<SpriteRenderer>();
				if (spriteRenderer != null)
				{
					spriteRenderer.sprite = GetComponent<SpriteRenderer>().sprite;
					spriteRenderer.flipX = GetComponent<SpriteRenderer>().flipX;
				}
			}

			unitDistanceLastFrame = distanceInUnits;
		}
	}


	void HandleLeftRight(float movement)
	{
		Vector2 temp = m_Rigidbody2D.velocity;
		temp.x = movement;
		m_Rigidbody2D.velocity = temp;
		Vector2 position = m_Rigidbody2D.position;
		position += m_Rigidbody2D.velocity * Time.fixedDeltaTime;
		m_Rigidbody2D.MovePosition(position);
	}

	void HandleGravity()
	{
		if (!m_Grounded)
		{
			var rbv = m_Rigidbody2D.velocity;
		}
		
	}

	void HandleAnimations(float move, bool crouch, bool jump)
	{
		if ((Mathf.Abs(move) > 0.001f))
			_playerAnimator.SetBool(Walking,true);
		else
			_playerAnimator.SetBool(Walking,false);

		_playerAnimator.SetBool(Crouching, crouch);
		_playerAnimator.SetBool(Grounded,m_Grounded);


	}


	void HandleCrouch()
	{
		
	}

	void HandleJump(bool jump)
	{
		
		if (!m_Grounded)
		{
			timeAccured = Time.time - jumpStartTime;
			var rbv = m_Rigidbody2D.velocity;
			
			//Gravity altering the velocity
			var desired_yvel = -_gravitySpeed; //* Time.deltaTime;
			rbv.y += desired_yvel;

			//Ensure not falling to fast
			var velocityY = rbv.y;
			if (velocityY < 0)
			{
				if (velocityY < max_fallSpeed) 
				{
					desired_yvel = -max_fallSpeed;
					rbv.y = desired_yvel;
				}
				if (Time.time < _timeLastBrickBuildAttemptMade + 0.2f)
				{
					rbv.y = 0;
				}
			}
			
			m_Rigidbody2D.velocity = rbv;
		}
		else
		{
			var rbv = m_Rigidbody2D.velocity;
			if(rbv.y<0.0f)
				rbv.y = 0.0f;
			m_Rigidbody2D.velocity = rbv;
		}

		if (jump)
		{
			if ((m_Grounded || UseCoyoteJump()))
			{
				_jumpStartedEvent.Invoke(transform.position);
				_coyoteJumpTimer = 0.0f;
				Debug.Log("Jump !");
				m_Grounded = false;
				var temp = m_Rigidbody2D.velocity;
				temp.y = m_JumpSpeed;
				m_Rigidbody2D.velocity = temp;

				//m_Rigidbody2D.velocity.y = JumpSpeed * Time.deltaTime;
				/*if(_isSuperJumpActive) 
					m_Rigidbody2D.AddForce(new Vector2(0f, m_JumpForce*superJumpMultiplier));
						else
					m_Rigidbody2D.AddForce(new Vector2(0f, m_JumpForce));*/

				jumpStartTime = Time.time;
				var result = MasterAudio.PlaySound("PlayerJump");
				Player._jumpFeedbacks?.PlayFeedbacks();
			}
		}

	}

	bool UseCoyoteJump()
	{
		var lastjumptime = Time.time - jumpStartTime;
		if (lastjumptime > _coyoteJumpAllowance+0.05f)
		{
			float AttemptedJumpTimeDiff = Time.time - _coyoteJumpTimer;
			if ((Mathf.Abs(AttemptedJumpTimeDiff) < _coyoteJumpAllowance))
			{
				_coyoteJumpTimer = 0.0f; //No Spamming
				return true;
			}
		}

		return false;
	}

	/*
	void HandleGravityReverse()
	{
		if (_isGravityReverseActive)
		{
			m_Rigidbody2D.gravityScale = -1f;
		}
		else
		{
			m_Rigidbody2D.gravityScale = 0f;
		}
	}*/

	private void Update()
	{
		
		//rgb_vel_y = m_Rigidbody2D.velocity.x;

	}

}
