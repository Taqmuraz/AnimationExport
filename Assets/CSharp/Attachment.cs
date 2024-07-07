using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Attachment : MonoBehaviour, IFramePreprocessor
{
    [SerializeField] Transform target;

    public void Call()
    {
        transform.position = target.position;
        transform.rotation = target.rotation;
    }
}
