using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;


public class PlayerObj : MonoBehaviour
{
    public SPUM_Prefabs _prefabs;
    public float _charMS;
    private PlayerState _currentState;

    public Vector3 _goalPos;
    public bool isAction = false;
    public Dictionary<PlayerState, int> IndexPair = new ();
    void Start()
    {
        if(_prefabs == null )
        {
            _prefabs = transform.GetChild(0).GetComponent<SPUM_Prefabs>();
            if(!_prefabs.allListsHaveItemsExist()){
                _prefabs.PopulateAnimationLists();
            }
        }
        _prefabs.OverrideControllerInit();
        foreach (PlayerState state in Enum.GetValues(typeof(PlayerState)))
        {
            IndexPair[state] = 0;
        }
    }
    public void SetStateAnimationIndex(PlayerState state, int index = 0){
        IndexPair[state] = index;
    }
    public void PlayStateAnimation(PlayerState state){
        _prefabs.PlayAnimation(state, IndexPair[state]);
    }
    void Update()
    {
        if(isAction) return;

        transform.position = new Vector3(transform.position.x,transform.position.y,transform.localPosition.y * 0.01f);
        switch(_currentState)
        {
            case PlayerState.IDLE:
            
            break;

            case PlayerState.MOVE:
            DoMove();
            break;
        }
        PlayStateAnimation(_currentState);

    }

    void DoMove()
    {
        Vector3 _dirVec  = _goalPos - transform.position ;
        Vector3 _disVec = (Vector2)_goalPos - (Vector2)transform.position ;
        if( _disVec.sqrMagnitude < 0.1f )
        {
            _currentState = PlayerState.IDLE;
            return;
        }
        Vector3 _dirMVec = _dirVec.normalized;
        transform.position += _dirMVec * _charMS * Time.deltaTime;
        

        if(_dirMVec.x > 0 ) _prefabs.transform.localScale = new Vector3(-1,1,1);
        else if (_dirMVec.x < 0) _prefabs.transform.localScale = new Vector3(1,1,1);
    }

    public void SetMovePos(Vector2 pos)
    {
        isAction = false;
        _goalPos = pos;
        _currentState = PlayerState.MOVE;
    }
}
