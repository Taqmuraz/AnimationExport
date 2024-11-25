using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomColor : MonoBehaviour {

	[SerializeField] Renderer target;

	// Use this for initialization
	void Start () {
		foreach (var mat in target.materials) mat.color = new Color(Random.value, Random.value, Random.value);
	}
	
	// Update is called once per frame
	void Update () {
		
	}
}
