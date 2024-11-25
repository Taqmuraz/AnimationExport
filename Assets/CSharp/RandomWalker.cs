using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomWalker : MonoBehaviour {

	[SerializeField] Rigidbody body;

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
		body.velocity = transform.forward * 6;
		transform.eulerAngles += Vector3.up * Time.deltaTime * 45;
	}
}
