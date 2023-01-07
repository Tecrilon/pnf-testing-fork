/*
 * PikminAI.cs
 * Created by: Ambrosia, Helodity
 * Created on: 30/4/2020 (dd/mm/yy)
 */

using System;
using UnityEditor.Rendering;
using UnityEngine;
using Random = UnityEngine.Random;

public enum PikminStates
{
	Idle,
	RunningTowards,
	Attacking,

	Carrying,
	Push,

	// Holding/Throwing States
	BeingHeld,
	Thrown,

	Dead,
	OnFire,
	Waiting,
}

// Immediate states after running towards another object/position
public enum PikminIntention
{
	Attack,
	Carry,
	Push,
	Idle, // AKA None
}

public class PikminAI : MonoBehaviour, IHealth, IComparable
{
	[Header("Components")]
	// Holds everything that makes a Pikmin unique
	public PikminObject _Data = null;

	[Header("Behaviour")]
	[SerializeField] float _LatchNormalOffset = 0.5f;
	[Space]
	[SerializeField] float _StoppingDistance = 0.5f;

	[Header("Idle")]
	[SerializeField] float _IdleTickRate = 0.3f;

	[Header("Object Avoidance")]
	[SerializeField] float _AvoidSphereSize = 0.5f;
	[SerializeField] float _PlayerPushScale = 30;
	[SerializeField] float _PikminPushScale = 15;

	[SerializeField] LayerMask _InteractableMask = 0;
	[SerializeField] LayerMask _RunTowardsMask = 0;
	[SerializeField] TrailRenderer _ThrowTrailRenderer = null;
	[SerializeField] LayerMask _PlayerAndPikminLayer = 0;

	[Header("VFX")]
	[SerializeField] GameObject _DeathParticle = null;
	[HideInInspector] public Transform _FormationPosition = null;

	[Header("Head Types")]
	[SerializeField] Transform _LeafSpawn = null;
	[SerializeField] PikminMaturity _StartingMaturity = PikminMaturity.Leaf;
	GameObject[] _HeadModels = null;
	public PikminMaturity _CurrentMaturity { get; set; } = PikminMaturity.Leaf;

	#region Debugging Variables
	[Header("==Debugging==")]
	public PikminStates _CurrentState = PikminStates.Idle;

	[Space, Header("Idle")]
	[SerializeField] Transform _TargetObject = null;
	[SerializeField] Collider _TargetObjectCollider = null;
	[SerializeField] PikminIntention _Intention = PikminIntention.Idle;
	[SerializeField] float _IdleTimer = 0.0f;

	// Carrying
	IPikminCarry _Carrying = null;

	[Space, Header("Attacking")]
	[SerializeField] Transform _AttackingTransform = null;
	IPikminAttack _Attacking = null;

	[Space, Header("Pushing")]
	IPikminPush _Pushing = null;
	[SerializeField] bool _PushReady = false;

	[Space, Header("On Fire")]
	[SerializeField] float _FireTimer = 0.0f;
	[SerializeField] float _WanderAngle = 0.0f;

	[Space, Header("Stats")]
	[SerializeField] PikminStatSpecifier _CurrentStatSpecifier = default;
	[SerializeField] float _CurrentMoveSpeed = 0;

	[Space, Header("Misc")]
	public Vector3 _AddedVelocity = Vector3.zero;
	[SerializeField] Vector3 _DirectionVector = Vector3.zero;
	[Space]
	[SerializeField] float _RagdollTime = 0;
	[Space]
	[SerializeField] LayerMask _MapMask;
	[SerializeField] LayerMask _AllMask;
	[SerializeField] float _MinimumY = -500;
	[Space]
	[SerializeField] Transform _LatchedTransform = null;
	public Vector3 _LatchedOffset = Vector3.zero;
	[SerializeField] float _HeldAudioTimer = 0;

	public bool _InSquad { get; private set; } = false;
	#endregion

	// Components
	AudioSource _AudioSource = null;
	Animator _Animator = null;
	Rigidbody _Rigidbody = null;
	CapsuleCollider _Collider = null;
	Transform _Transform = null;
	Transform _PlayerTransform = null;

	#region Interface Methods

	float IHealth.GetCurrentHealth()
	{
		return 1;
	}

	float IHealth.GetMaxHealth()
	{
		return 1;
	}

	void IHealth.SetHealth(float h) { if (h <= 0) { Die(); } }

	float IHealth.SubtractHealth(float h)
	{
		//Pikmin don't have health so they die no matter what
		Die();
		return 0;
	}

	float IHealth.AddHealth(float h)
	{
		return 1;
	}

	#endregion

	#region Unity Methods
	void OnEnable()
	{
		GameObject fObject = new($"{name}_{GetInstanceID()}_formation_pos");
		_FormationPosition = fObject.transform;
		_Transform = transform;
	}

	void Awake()
	{
		_Rigidbody = GetComponent<Rigidbody>();
		_Animator = GetComponent<Animator>();
		_Collider = GetComponent<CapsuleCollider>();

		_AudioSource = GetComponent<AudioSource>();
		_AudioSource.volume = _Data._AudioVolume;

		_ThrowTrailRenderer.enabled = false;

		_IdleTimer = Random.Range(0.02f, _IdleTickRate - (_IdleTickRate / 10));

		_CurrentStatSpecifier = PikminStatSpecifier.OnField;
		PikminStatsManager.Add(_Data._PikminColour, _CurrentMaturity, _CurrentStatSpecifier);

		_HeadModels = new GameObject[(int)PikminMaturity.Size];
		_HeadModels[0] = Instantiate(_Data._Leaf);
		_HeadModels[0].transform.parent = _LeafSpawn;
		_HeadModels[0].transform.localPosition = Vector3.zero;

		_HeadModels[1] = Instantiate(_Data._Bud);
		_HeadModels[1].transform.parent = _LeafSpawn;
		_HeadModels[1].transform.localPosition = Vector3.zero;

		_HeadModels[2] = Instantiate(_Data._Flower);
		_HeadModels[2].transform.parent = _LeafSpawn;
		_HeadModels[2].transform.localPosition = Vector3.zero;

		SetMaturity(_StartingMaturity);

		GameManager.OnPauseEvent += OnPauseEvent;
	}

	void OnPauseEvent(PauseType t)
	{
		if (t == PauseType.Paused)
		{
			if (_Animator != null && _Rigidbody != null)
			{
				_Animator.SetBool("Walking", false);
				_Rigidbody.isKinematic = true;
			}
		}
		else
		{
			_Rigidbody.isKinematic = false;
		}
	}

	void Start()
	{
		_FormationPosition.SetParent(Player._Instance._PikminController._FormationCenter.transform);
		_PlayerTransform = Player._Instance.transform;
	}

	void Update()
	{
		if (GameManager.IsPaused && GameManager.PauseType != PauseType.OnlyPikminActive)
		{
			return;
		}

		if (_Transform.position.y < _MinimumY)
		{
			_Rigidbody.velocity = Vector3.zero;
			_Transform.position = _PlayerTransform.position;
		}

		MaintainLatch();

		switch (_CurrentState)
		{
			case PikminStates.Idle:
				HandleIdle();
				break;
			case PikminStates.Attacking:
				HandleAttacking();
				break;
			case PikminStates.Push:
				HandlePushing();
				break;
			case PikminStates.OnFire:
				HandleOnFire();
				break;
			case PikminStates.Dead:
				HandleDeath();
				break;
			case PikminStates.BeingHeld:
				if (_HeldAudioTimer >= 0.5f)
				{
					_AudioSource.pitch = Random.Range(0.75f, 1.25f);
					PlaySound(_Data._HeldNoise, Random.Range(0, 0.15f));
					_HeldAudioTimer = 0;
				}

				_HeldAudioTimer += Time.deltaTime;
				break;

			case PikminStates.Carrying:
			case PikminStates.Thrown:
			case PikminStates.RunningTowards:
			case PikminStates.Waiting:
			default:
				break;
		}

		HandleAnimations();
	}


	void FixedUpdate()
	{
		if (GameManager.IsPaused && GameManager.PauseType != PauseType.OnlyPikminActive)
		{
			return;
		}

		MaintainLatch();

		switch (_CurrentState)
		{
			case PikminStates.BeingHeld:
			case PikminStates.Thrown:
			case PikminStates.Attacking:
			case PikminStates.Carrying:
				return;
			case PikminStates.RunningTowards:
				HandleRunningTowards();
				break;
		}

		RepelPikminAndPlayers();

		float storedY = _Rigidbody.velocity.y;
		_Rigidbody.velocity = _DirectionVector + _AddedVelocity;
		_DirectionVector = Vector3.up * storedY;

		_AddedVelocity = Vector3.Lerp(_AddedVelocity, Vector3.zero, 10 * Time.deltaTime);
	}

	void OnCollisionEnter(Collision collision)
	{
		OnCollisionHandle(collision.collider);
	}

	void OnCollisionStay(Collision collision)
	{
		OnCollisionHandle(collision.collider);
	}

	void OnCollisionExit(Collision collision)
	{
		OnCollisionHandle(collision.collider);
	}

	#endregion

	#region States

	void HandleAnimations()
	{
		_Animator.SetBool("Thrown", _CurrentState == PikminStates.Thrown);
		_Animator.SetBool("Attacking", _CurrentState == PikminStates.Attacking);

		switch (_CurrentState)
		{
			case PikminStates.Idle:
			case PikminStates.RunningTowards:
				{
					Vector2 horizonalVelocity = new Vector2(_Rigidbody.velocity.x, _Rigidbody.velocity.z);
					_Animator.SetBool("Walking", horizonalVelocity.magnitude >= 0.1f);

					// Keep Pikmin upright
					Vector3 euler = _Transform.eulerAngles;
					_Transform.rotation = Quaternion.Euler(0, euler.y, 0);
					break;
				}

			case PikminStates.Push:
			case PikminStates.Carrying:
				_Animator.SetBool("Walking", true);
				break;

			default:
				break;
		}
	}

	void CarryoutIntention()
	{
		PikminStates previousState = _CurrentState;

		// Run intention-specific logic (attack = OnAttackStart for the target object)
		switch (_Intention)
		{
			case PikminIntention.Attack:
				_Attacking = _TargetObject.GetComponentInParent<IPikminAttack>();
				if (!_Attacking.IsAttackAvailable())
				{
					_Intention = PikminIntention.Idle;
					ChangeState(PikminStates.Idle);
					return;
				}

				_Attacking.OnAttackStart(this);

				_AttackingTransform = _TargetObject;

				LatchOnto(_AttackingTransform);
				ChangeState(PikminStates.Attacking);
				break;
			case PikminIntention.Carry:
				_Carrying = _TargetObject.GetComponentInParent<IPikminCarry>();
				_Carrying.OnCarryStart(this);
				break;
			case PikminIntention.Push:
				if (!IsGrounded())
				{
					_Intention = PikminIntention.Idle;
					ChangeState(PikminStates.Idle);
					return;
				}

				_Pushing = _TargetObject.GetComponentInParent<IPikminPush>();
				if (!_Pushing.OnPikminAdded(this))
				{
					_Intention = PikminIntention.Idle;
					ChangeState(PikminStates.Idle);
					return;
				}
				break;
			case PikminIntention.Idle:
				ChangeState(PikminStates.Idle);
				break;
			default:
				break;
		}

		if (previousState == _CurrentState && _CurrentState != PikminStates.Idle)
		{
			ChangeState(PikminStates.Idle);
		}

		_Intention = PikminIntention.Idle;
	}

	void HandleIdle()
	{
		// Don't do anything until we hit the floor!
		if (!IsGrounded())
		{
			return;
		}

		// Checks if we're in squad anyways, so it's a safe check to make sure
		// we're not actually in squad (known bug)
		RemoveFromSquad();

		_IdleTimer += Time.deltaTime;
		if (_IdleTimer <= _IdleTickRate)
		{
			return;
		}

		_IdleTimer = 0;

		// Scan for the closest target object and then run towards it
		Collider closestCol = null;
		float curClosestDist = float.PositiveInfinity;
		Collider[] objects = Physics.OverlapSphere(_Transform.position, _Data._SearchRadius, _InteractableMask | _RunTowardsMask);
		foreach (Collider collider in objects)
		{
			IPikminInteractable interactableComponent = collider.GetComponentInParent<IPikminInteractable>();
			if (interactableComponent == null)
			{
				continue;
			}

			PikminIntention currentIntention = interactableComponent.IntentionType;

			if (currentIntention == PikminIntention.Attack &&
				!collider.GetComponentInParent<IPikminAttack>().IsAttackAvailable())
			{
				return;
			}

			if (currentIntention == PikminIntention.Carry)
			{
				IPikminCarry toCarry = collider.GetComponentInParent<IPikminCarry>();

				if (!toCarry.PikminSpotAvailable())
				{
					continue;
				}

				_Carrying = toCarry;
			}
			else if (currentIntention == PikminIntention.Push)
			{
				IPikminPush toPush = collider.GetComponentInParent<IPikminPush>();

				if (!toPush.PikminSpotAvailable())
				{
					continue;
				}

				_Pushing = toPush;
			}
			else
			{
				// Determine if the collider is on the same level as us
				Vector3 direction = MathUtil.DirectionFromTo(_Transform.position, collider.ClosestPoint(_Transform.position));
				if (!Physics.Raycast(_Transform.position, direction, out RaycastHit hit, _Data._SearchRadius,
					_AllMask, QueryTriggerInteraction.Ignore)
					|| hit.collider != collider)
				{
					// FALLBACK: we'll use the global origin position instead of the closest point
					direction = MathUtil.DirectionFromTo(_Transform.position, collider.transform.position);
					if (!Physics.Raycast(_Transform.position, direction, out hit, _Data._SearchRadius,
						_AllMask, QueryTriggerInteraction.Ignore)
						|| hit.collider != collider)
					{
						continue;
					}
				}
			}

			float distance = MathUtil.DistanceTo(_Transform.position, collider.transform.position);
			if (distance < curClosestDist)
			{
				closestCol = collider;
				curClosestDist = distance;
				_Intention = currentIntention;
			}
		}

		if (closestCol != null)
		{
			// We can move to the target object, and it is an interactable, so set our target object
			ChangeState(PikminStates.RunningTowards);
			_TargetObject = closestCol.transform;
			_TargetObjectCollider = closestCol;
		}
	}

	void HandleRunningTowards()
	{
		if (!_InSquad && _TargetObject == null)
		{
			ChangeState(PikminStates.Idle);
		}
		else if (_InSquad)
		{
			MoveTowards(_FormationPosition.position);
		}
		else
		{
			MoveTowardsTarget();
		}

		// If whatever we're running after doesn't have any spots for us to go to,
		// then we'll just go back to idle
		switch (_Intention)
		{
			case PikminIntention.Carry when _Carrying != null:
				if (!_Carrying.PikminSpotAvailable())
				{
					_Intention = PikminIntention.Idle;
					ChangeState(PikminStates.Idle);
				}
				break;
			case PikminIntention.Push when _Pushing != null:
				if (!_Pushing.PikminSpotAvailable())
				{
					_Intention = PikminIntention.Idle;
					ChangeState(PikminStates.Idle);
				}
				break;
		}
	}

	void HandleAttacking()
	{
		// The object we were attacking has died, so we can go back to being idle
		if (_Attacking == null || _AttackingTransform == null)
		{
			ChangeState(PikminStates.Idle);
			return;
		}
	}

	void HandlePushing()
	{
		if (_Pushing == null)
		{
			ChangeState(PikminStates.Idle);
			return;
		}

		Vector3 pushPos = _Pushing.GetPushPosition(this);

		// GetPushPosition may change our state, so we'll exit early if that's the case
		if (_CurrentState == PikminStates.Idle)
		{
			return;
		}

		if (!_PushReady)
		{
			_Pushing.OnPikminReady(this);
			_PushReady = true;
		}

		if (MathUtil.DistanceTo(_Transform.position, pushPos, false) <= 1.0f)
		{
			_Transform.position = Vector3.Lerp(_Transform.position, pushPos, 17.5f * Time.deltaTime);
		}
		else
		{
			MoveTowards(pushPos, false);
		}
	}

	void HandleDeath()
	{
		RemoveFromSquad(PikminStates.Dead);

		if (_RagdollTime > 0)
		{
			_Rigidbody.constraints = RigidbodyConstraints.None;
			_Rigidbody.isKinematic = false;
			_Rigidbody.useGravity = true;
			_RagdollTime -= Time.deltaTime;
			return;
		}

		PikminStatsManager.Remove(_Data._PikminColour, _CurrentMaturity, _CurrentStatSpecifier);
		AudioSource.PlayClipAtPoint(_Data._DeathNoise, _Transform.position, _Data._AudioVolume);

		// Create the soul gameobject, and play the death noise
		if (_DeathParticle != null)
		{
			ParticleSystem soul = Instantiate(_DeathParticle, _Transform.position, Quaternion.Euler(-90, 0, 0)).GetComponent<ParticleSystem>();
			ParticleSystem.MainModule soulEffect = soul.main;
			soulEffect.startColor = _Data._DeathSpiritPikminColour;
			Destroy(soul.gameObject, 5);
		}

		if (PikminStatsManager.GetTotalInAllOnions() + PikminStatsManager.GetTotalOnField() == 0)
		{
			Player._Instance.PikminExtinction();
		}

		// Remove the object
		Destroy(gameObject);
	}

	void HandleOnFire()
	{
		_FireTimer += Time.deltaTime;

		// RIP
		if (_FireTimer > 5.0f)
		{
			Die();
			return;
		}

		float yRotation = Mathf.Lerp(_Transform.eulerAngles.y, _WanderAngle, 1.5f * Time.deltaTime);
		if (Mathf.Abs(yRotation - _WanderAngle) <= 15.0f)
		{
			_WanderAngle = Random.Range(0.0f, 360.0f);
		}
		_Transform.rotation = Quaternion.Euler(0, yRotation, 0);

		MoveTowards(_Transform.forward, false);
	}
	#endregion

	#region Misc
	void MoveTowards(Vector3 position, bool stopEarly = true)
	{
		// Rotate to look at the object we're moving towards
		Vector3 delta = MathUtil.DirectionFromTo(_Transform.position, position);

		if (delta != Vector3.zero)
		{
			_Transform.rotation = _InSquad
								// Rotate towards player
								? Quaternion.Lerp(_Transform.rotation, Quaternion.LookRotation(MathUtil.DirectionFromTo(_Transform.position, _PlayerTransform.position)), _Data._RotationSpeed * Time.deltaTime)
								// Rotate towards actual object
								: Quaternion.Lerp(_Transform.rotation, Quaternion.LookRotation(delta), _Data._RotationSpeed * Time.deltaTime);
		}

		if (stopEarly && MathUtil.DistanceTo(_Transform.position, position, false) < _StoppingDistance)
		{
			_CurrentMoveSpeed = Mathf.Lerp(_CurrentMoveSpeed, 0, 15 * Time.deltaTime);
		}
		else
		{
			// To prevent instant, janky movement we step towards the resultant max speed according to _Acceleration
			_CurrentMoveSpeed = Mathf.Lerp(_CurrentMoveSpeed, _Data.GetMaxSpeed(_CurrentMaturity), _Data.GetAcceleration(_CurrentMaturity) * Time.deltaTime);
		}

		Vector3 newVelocity = delta * _CurrentMoveSpeed;
		newVelocity.y = _Rigidbody.velocity.y;
		_DirectionVector = newVelocity;
	}

	void MoveTowardsTarget()
	{
		// Move a little bit forward so that we guarantee
		// an intersection & so that we don't stop too early
		Vector3 closestPoint = ClosestPointOnTarget(_TargetObject, _TargetObjectCollider, _Data._SearchRadius);
		MoveTowards(closestPoint, false);
	}

	void RepelPikminAndPlayers()
	{
		if (_CurrentState != PikminStates.RunningTowards && _CurrentState != PikminStates.Idle)
		{
			return;
		}

		Collider[] objects = Physics.OverlapSphere(_Transform.position, _AvoidSphereSize, _PlayerAndPikminLayer);
		foreach (Collider collider in objects)
		{
			if (collider.CompareTag("Pikmin"))
			{
				Vector3 direction = MathUtil.DirectionFromTo(collider.transform.position, _Transform.position);
				if (direction.sqrMagnitude <= 0.01f)
				{
					direction.x = Random.Range(-0.025f, 0.025f);
					direction.z = Random.Range(-0.025f, 0.025f);
				}
				_AddedVelocity += _PikminPushScale * Time.fixedDeltaTime * direction;
			}
			else if (collider.CompareTag("Player"))
			{
				Vector3 direction = MathUtil.DirectionFromTo(collider.transform.position, _Transform.position);
				_AddedVelocity += _PlayerPushScale * Time.fixedDeltaTime * direction;

				AddToSquad();
			}
		}
	}

	void OnCollisionHandle(Collider collision)
	{
		if (_InSquad)
		{
			return;
		}

		if (_CurrentState == PikminStates.Thrown)
		{
			// Just landed from a throw, check if we're on something we interact with
			if (collision.CompareTag("PikminInteract"))
			{
				_TargetObject = collision.transform;
				_TargetObjectCollider = collision;
				_Intention = collision.GetComponentInParent<IPikminInteractable>().IntentionType;
				CarryoutIntention();
			}
			else if (!collision.CompareTag("Pikmin") && !collision.CompareTag("Player"))
			{
				ChangeState(PikminStates.Idle);
			}
		}
		else if (_CurrentState == PikminStates.RunningTowards)
		{
			// If we've been running towards something, we've touched it and now we
			// can carryout our intention
			if (_TargetObjectCollider != null && _TargetObjectCollider == collision
				&& _TargetObjectCollider.gameObject.layer != _RunTowardsMask)
			{
				_Intention = _TargetObjectCollider.GetComponentInParent<IPikminInteractable>().IntentionType;
				CarryoutIntention();
			}
		}
		else if (collision.CompareTag("Player")
			&& _CurrentState != PikminStates.Push
			&& _CurrentState != PikminStates.Carrying
			&& _CurrentState != PikminStates.Attacking
			&& _CurrentState != PikminStates.Thrown)
		{
			AddToSquad();
		}
	}

	Vector3 ClosestPointOnTarget(Transform target, Collider collider = null, float maxDistance = float.PositiveInfinity)
	{
		// Check if there is a collider for the target object we're running to
		if (collider == null)
		{
			return target.position;
		}

		Vector3 closestPoint = collider.ClosestPoint(_Transform.position);
		Vector3 direction = MathUtil.DirectionFromTo(_Transform.position, closestPoint);

		// If we can hit the target and it's in our straight-on eye line
		if (Physics.Raycast(_Transform.position, direction, out RaycastHit hit, maxDistance, _AllMask, QueryTriggerInteraction.Ignore)
			&& hit.collider == collider)
		{
			return hit.point;
		}

		float yOffset = Mathf.Abs(closestPoint.y - _Transform.position.y);
		if (yOffset < 3.5f)
		{
			return closestPoint;
		}

		return target.position;
	}

	void MaintainLatch()
	{

		if (_LatchedTransform == null)
		{
			return;
		}

		Vector3 pos = _LatchedTransform.position + _LatchedOffset;
		bool useY = _CurrentState == PikminStates.Carrying;

		if (_TargetObjectCollider != null)
		{
			Vector3 thisPos = _Transform.position;
			Vector3 nextPos = _TargetObjectCollider.ClosestPoint(pos);
			Vector3 direct = MathUtil.DirectionFromTo(thisPos, nextPos, !useY);

			if (Physics.Raycast(thisPos, direct, out RaycastHit info, 1.5f + _LatchNormalOffset)
				&& info.transform == _LatchedTransform)
			{
				Vector3 resultPos = nextPos + (info.normal * _LatchNormalOffset);
				if (Physics.OverlapSphere(resultPos, 0.5f, _MapMask).Length == 0)
				{
					pos = Vector3.Lerp(thisPos, resultPos, 35 * Time.deltaTime);
				}
			}
		}
		else
		{
			_TargetObjectCollider = _LatchedTransform.GetComponent<Collider>();
		}

		Vector3 directionFromPosToObj = MathUtil.DirectionFromTo(pos, _LatchedTransform.position, !useY);
		_Transform.SetPositionAndRotation(pos, Quaternion.LookRotation(directionFromPosToObj));
	}

	#endregion

	#region Public Function
	public PikminColour GetColour() => _Data._PikminColour;

	public int CompareTo(object obj)
	{
		PikminAI other = obj as PikminAI;

		int thisColour = (int)GetColour();
		int otherColour = (int)other.GetColour();

		if ((int)Player._Instance._PikminController._SelectedThrowPikmin == thisColour)
		{
			return 1;
		}

		if (thisColour != otherColour)
		{
			return -1;
		}

		return 0;
	}

	public void SetMaturity(PikminMaturity m)
	{
		PikminStatsManager.Remove(_Data._PikminColour, _CurrentMaturity, _CurrentStatSpecifier);
		_CurrentMaturity = m;
		PikminStatsManager.Add(_Data._PikminColour, _CurrentMaturity, _CurrentStatSpecifier);

		// Get the headtype we want to enable
		int type = (int)_CurrentMaturity;
		// Iterate over all of the heads, and activate the one that matches the type we want
		for (int i = 0; i < (int)PikminMaturity.Flower + 1; i++)
		{
			_HeadModels[i].SetActive(i == type);
		}
	}

	public void ChangeState(PikminStates newState)
	{
		// There's no saving pikmin from death
		if (_CurrentState == PikminStates.Dead)
		{
			return;
		}

		// cleaning up from OLD
		switch (_CurrentState)
		{
			case PikminStates.RunningTowards:
				_Animator.SetBool("Walking", false);
				_TargetObject = null;
				_TargetObjectCollider = null;
				break;
			case PikminStates.Idle when _TargetObject != null:
				_TargetObject = null;
				_TargetObjectCollider = null;
				break;
			case PikminStates.OnFire:
				_FireTimer = 0.0f;
				_WanderAngle = Random.Range(0.0f, 360.0f);
				break;
			case PikminStates.Attacking:
				LatchOnto(null);

				_DirectionVector = Vector3.down * 2;
				_Animator.SetBool("Attacking", false);

				_Attacking?.OnAttackEnd(this);

				_AttackingTransform = null;
				_Attacking = null;

				_Transform.eulerAngles = new Vector3(0, _Transform.eulerAngles.y, 0);
				break;
			case PikminStates.Carrying:
				LatchOnto(null);
				_Carrying?.OnCarryLeave(this);
				_Carrying = null;
				break;
			case PikminStates.Push:
				_Pushing?.OnPikminLeave(this);
				_Pushing = null;
				_PushReady = false;
				break;
			case PikminStates.Thrown:
				_ThrowTrailRenderer.enabled = false;
				break;
			case PikminStates.BeingHeld:
				_AudioSource.pitch = 1.0f;
				break;
			default:
				break;
		}

		_CurrentState = newState;

		// initialising NEW
		switch (newState)
		{
			case PikminStates.Thrown:
				_ThrowTrailRenderer.enabled = true;
				break;
			case PikminStates.Carrying:
				PlaySoundForced(_Data._CarryAddNoise);
				break;
			case PikminStates.OnFire:
				_FireTimer = 0.0f;
				_WanderAngle = Random.Range(0.0f, 360.0f);
				break;
			case PikminStates.Idle:
				LatchOnto(null);

				_Transform.eulerAngles = new Vector3(0, _Transform.eulerAngles.y, 0);

				_AudioSource.volume = 0.01f;
				_AudioSource.clip = _Data._IdleNoise;
				_AudioSource.Play();
				break;
		}
	}

	public void StartThrowHold()
	{
		_Rigidbody.isKinematic = true;
		_Rigidbody.useGravity = false;
		_Collider.isTrigger = true;

		if (_Animator.GetBool("Walking") == true)
		{
			_Animator.SetBool("Walking", false);
		}

		_Animator.SetBool("Holding", true);

		ChangeState(PikminStates.BeingHeld);
	}

	// We've been thrown!
	public void EndThrowHold()
	{
		_Rigidbody.isKinematic = false;
		_Rigidbody.useGravity = true;
		_Collider.isTrigger = false;

		_Animator.SetBool("Holding", false);

		_InSquad = false;
		_CurrentStatSpecifier = PikminStatSpecifier.OnField;
		_TargetObject = null;
		ChangeState(PikminStates.Thrown);

		PlaySoundForced(_Data._ThrowNoise);

		PikminStatsManager.RemoveFromSquad(this, _Data._PikminColour, _CurrentMaturity);
		PikminStatsManager.ReassignFormation();
	}

	/// <summary>
	/// Latches the Pikmin onto an object
	/// </summary>
	/// <param name="obj">Object</param>
	/// <param name="onlyY">Rotate only to look at things</param>
	public void LatchOnto(Transform obj)
	{
		_LatchedTransform = obj;
		_TargetObject = obj;

		if (obj != null)
		{
			_Rigidbody.isKinematic = true;
			_Collider.isTrigger = true;

			_TargetObject = obj;
			_TargetObjectCollider = obj.GetComponent<Collider>();

			if (!IsGrounded())
			{
				Vector3 closestPosition = ClosestPointOnTarget(_TargetObject, _TargetObjectCollider);
				Vector3 dirToClosestPos = MathUtil.DirectionFromTo(_Transform.position, closestPosition, true);
				if (Physics.Raycast(_Transform.position, dirToClosestPos, out RaycastHit info, 1.5f, _InteractableMask, QueryTriggerInteraction.Collide)
					&& info.collider == _TargetObjectCollider)
				{
					Vector3 point = info.point;

					_Transform.LookAt(point);
					_Transform.position = point + info.normal * _LatchNormalOffset;
				}
			}

			_Transform.parent = _LatchedTransform;
			_LatchedOffset = _Transform.position - _LatchedTransform.position;
		}
		else
		{
			_Rigidbody.isKinematic = false;
			_Collider.isTrigger = false;
			_Transform.parent = null;

			_Transform.eulerAngles = new Vector3(0, _Transform.eulerAngles.y, _Transform.eulerAngles.z);
		}
	}

	public void AddToSquad()
	{
		if (_InSquad || _CurrentState == PikminStates.Dead || _CurrentState == PikminStates.Thrown
			|| (_CurrentState == PikminStates.Push && !_Pushing.PikminSpotAvailable()))
		{
			return;
		}

		_InSquad = true;
		_CurrentStatSpecifier = PikminStatSpecifier.InSquad;

		ChangeState(PikminStates.RunningTowards);

		PikminStatsManager.AddToSquad(this, _Data._PikminColour, _CurrentMaturity);
		PikminStatsManager.ReassignFormation();
	}

	public void RemoveFromSquad(PikminStates to = PikminStates.Idle)
	{
		if (!_InSquad)
		{
			return;
		}

		_InSquad = false;
		_CurrentStatSpecifier = PikminStatSpecifier.OnField;

		_TargetObject = null;
		ChangeState(to);

		PikminStatsManager.RemoveFromSquad(this, _Data._PikminColour, _CurrentMaturity);
		PikminStatsManager.ReassignFormation();
	}

	public void Die(float ragdollTimer = 0)
	{
		if (_CurrentState == PikminStates.Dead)
		{
			return;
		}

		_RagdollTime = ragdollTimer;
		ChangeState(PikminStates.Dead);
	}

	public void WaterEnter()
	{
		if (_Data._PikminColour != PikminColour.Blue)
		{
			Die(0.5f);
		}
	}

	public void WaterLeave()
	{
		Debug.Log("Left water");
	}

	public void InteractFire()
	{
		if (_Data._PikminColour == PikminColour.Red)
		{
			return;
		}

		ChangeState(PikminStates.OnFire);
	}

	public bool IsGrounded(float distTo = 0.2f)
	{
		return Physics.Raycast(_Transform.position, Vector3.down, distTo, _MapMask, QueryTriggerInteraction.Ignore);
	}

	// CALLED BY PIKMIN ANIMATION - ATTACK
	public void ANIM_Attack()
	{
		if (_Attacking != null)
		{
			_Attacking.OnAttackRecieve(_Data._AttackDamage);
			PlaySoundForced(_Data._AttackHitNoise);
		}
	}

	public void PlaySound(AudioClip clip, float delay = 0)
	{
		if (_AudioSource.clip != clip)
		{
			_AudioSource.clip = clip;
		}

		if (_AudioSource.isPlaying)
		{
			return;
		}

		_AudioSource.volume = _Data._AudioVolume;
		if (delay != 0)
		{
			_AudioSource.PlayDelayed(delay);
		}
		else
		{
			_AudioSource.Play();
		}
	}

	public void PlaySoundForced(AudioClip clip)
	{
		if (_AudioSource.clip != clip)
		{
			_AudioSource.clip = clip;
			_AudioSource.Stop();
		}

		_AudioSource.volume = _Data._AudioVolume;
		_AudioSource.Play();
	}
	#endregion
}
