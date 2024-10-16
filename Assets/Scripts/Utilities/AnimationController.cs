﻿using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
///   Handles Unity Animator and Animations as part of a single "class"
/// </summary>
[Serializable]
class AnimationController
{
	public Animator _ParentAnimator;

	List<AnimationClip> _Animations = new();
	int _CurrentState;
	bool _FinishCurrent;

	/// <summary>
	///   Adds a state to the list of Animations
	/// </summary>
	/// <returns></returns>
	public int AddState(AnimationClip anim)
	{
		_Animations.Add(anim);
		return _Animations.Count - 1;
	}


	/// <summary>
	///   Changes the current state of the animator to that of animIdx
	/// </summary>
	public void ChangeState(int animIdx, bool finishAnim = false, bool overRide = false)
	{
		if (_CurrentState == animIdx && !overRide)
		{
			return;
		}

		// If we aren't overriding and we're wanting to finish the current animation
		if (!overRide && _FinishCurrent)
		{
			// Check the animator has finished the clip
			AnimatorStateInfo stateInfo = _ParentAnimator.GetCurrentAnimatorStateInfo(0);

			if (stateInfo.IsName(_Animations[animIdx].name))
			{
				// It hasn't finished the clip yet, so we return and wait
				return;
			}

			_FinishCurrent = false;
		}
		else if (finishAnim)
		{
			_FinishCurrent = true;
		}

		_CurrentState = animIdx;
		_ParentAnimator.CrossFade(_Animations[animIdx].name, 0.05f);
	}
}
