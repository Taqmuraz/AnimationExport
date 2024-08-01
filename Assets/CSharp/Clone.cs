using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Clone : MonoBehaviour
{
	[SerializeField] GameObject target;
	[SerializeField] int times;

	void Start ()
	{
		for(int i = 0; i < times; i++)
		{
			int x = i % 10;
			int y = 0;
			int z = i / 10;
			var obj = GameObject.Instantiate(target);
			obj.transform.position = new Vector3(x, y, z) * 2;
			obj.GetComponent<Animator>().CrossFade("Attack", i * 0.1f);
		}
	}
}
