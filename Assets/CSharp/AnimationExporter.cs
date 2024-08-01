using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public class AnimationExporter : MonoBehaviour
{
	[SerializeField] AnimationClip[] animationClips;
	[SerializeField] string[] outputPaths;
	[SerializeField] string directoryName;
	[SerializeField] GameObject defaultStance;
	[SerializeField] Vector3 scaleMultiplier;

	struct TransformState
	{
		public Vector3 position;
		public Vector3 rotation;
		public Vector3 scale;

		public static TransformState Create(Transform transform)
		{
			var t = new TransformState();
			t.position = transform.localPosition;
			t.rotation = transform.localEulerAngles;
			t.scale = transform.localScale;
			return t;
		}

		public bool Equals(TransformState t)
		{
			return position == t.position && rotation == t.rotation && scale == t.scale;
		}

        public override int GetHashCode()
        {
			int hash = 17;
			hash = hash * 23 + position.GetHashCode();
			hash = hash * 23 + rotation.GetHashCode();
			hash = hash * 23 + scale.GetHashCode();
			return hash;
		}
    }

	Dictionary<Transform, TransformState> states = new Dictionary<Transform, TransformState>();

	void Start()
	{
		var descendants = Descendants(transform);
		states = descendants.ToDictionary(t => t, t => TransformState.Create(t));
	}

	void ResetStates()
	{
		foreach (var d in Descendants(transform))
		{
			var t = states[d];
			d.localPosition = t.position;
			d.localEulerAngles = t.rotation;
		}
	}

	static IEnumerable<Transform> Descendants(Transform transform)
	{
		return Enumerable.Range(0, transform.childCount).Select(transform.GetChild).SelectMany(t => new Transform[] { t }.Concat(Descendants(t)));
	}

	class Bone
	{
		public readonly TransformState transform;
		public readonly string name;
		public readonly float time;

        public Bone(TransformState transform, string name, float time)
        {
            this.transform = transform;
            this.name = name;
			this.time = time;
        }
    }

	class Point
	{
		public readonly float time;
		public readonly TransformState transform;

        public Point(float time, TransformState transform)
        {
            this.time = time;
            this.transform = transform;
        }
    }

	class Line
	{
		public readonly string name;
		public readonly Point[] points;

        public Line(string name, Point[] points)
        {
            this.name = name;
            this.points = points;
        }
    }

	class PointComparer : IEqualityComparer<Point>
	{
        public bool Equals(Point x, Point y)
        {
			return x.transform.Equals(y.transform);
        }

        public int GetHashCode(Point obj)
        {
			return obj.transform.GetHashCode();
        }
    }

	static Line CompressLine(Line line)
	{
		var points = line.points.Distinct(new PointComparer()).ToArray();
		if (points.Length == 1) return new Line(line.name, points);
		else return line;
	}

	Bone TransformFrame(Transform transform, float time)
	{
		var t = TransformState.Create(transform);
		t.position.x *= scaleMultiplier.x;
		t.position.y *= scaleMultiplier.y;
		t.position.z *= scaleMultiplier.z;
		return new Bone(t, transform.name, time);
	}
	Bone[] AnimationFrame(float time, Transform[] bones)
	{
		return bones.Select(b => TransformFrame(b, time)).ToArray();
	}
	static Line[] AnimationLines(List<Bone[]> frames)
	{
		if (frames.Count == 0) return new Line[0];
		var map = new Dictionary<string, List<Point>>();
		foreach (var bone in frames.SelectMany(f => f))
		{
			List<Point> list;
			if (!map.TryGetValue(bone.name, out list))
			{
				list = new List<Point>();
				map[bone.name] = list;
			}
			list.Add(new Point(bone.time, bone.transform));
		}
		return map.Select(p => new Line(p.Key, p.Value.ToArray())).ToArray();
	}

	string TransformPath(Transform t, Transform root)
	{
		List<string> p = new List<string>();
		while (t != root)
		{
			p.Add(t.name);
			t = t.parent;
		}
		return string.Join("/", p.ToArray());
	}

	[ContextMenu("Reset pose")]
	void ResetPose()
	{
		var dest_a = Descendants(gameObject.transform);
		var dest_b = Descendants(defaultStance.transform).ToDictionary(t => TransformPath(t, defaultStance.transform));

		foreach (var a in dest_a)
		{
			Transform b;
			if (dest_b.TryGetValue(TransformPath(a, gameObject.transform), out b))
			{
				a.localPosition = b.localPosition;
				a.localRotation = b.localRotation;
			}
		}
	}

	[ContextMenu("Create animation")]
	void CreateAnimation ()
	{
		if (!EditorApplication.isPlaying)
		{
			Debug.Log("Enter play mode first");
			return;
		}
		
		StartCoroutine(CreateAnimations(gameObject));
	}

	IEnumerator CreateAnimations(GameObject gameObject)
	{
		foreach (var animationClip in animationClips)
		{
			yield return StartCoroutine(CreateAnimation(gameObject, animationClip));
			ResetStates();
			yield return new WaitForEndOfFrame();
		}
	}

	static List<string> vectors = new List<string>();

	IEnumerator CreateAnimation(GameObject gameObject, AnimationClip animationClip)
	{
		var bones = Descendants(gameObject.transform).Where(t => t.gameObject.tag != "Ignore").ToArray();
		var framePreprocessors = GetComponentsInChildren<IFramePreprocessor>();
		Action proc = framePreprocessors
			.Select<IFramePreprocessor, Action>(f => f.Call)
			.Aggregate<Action, Action>(() => { }, (a, b) => a + b);

		var frameCount = (int)(animationClip.length * animationClip.frameRate);
		if (animationClip.length == 0) frameCount = 1;

		List<Bone[]> frames = new List<Bone[]>();

		foreach (var f in Enumerable.Range(0, frameCount))
		{
			var t = f / (float)frameCount;
			animationClip.SampleAnimation(gameObject, t);
			yield return new WaitForEndOfFrame();
			proc();
			frames.Add(AnimationFrame(t, bones));
		}

		var lines = AnimationLines(frames).Select(CompressLine).ToArray();

		yield return new WaitForEndOfFrame();

		var writer = new StringBuilder();
		vectors.Clear();
		WriteAnimation(s => writer.Append(s), animationClip.name, animationClip.length, lines);
		Debug.Log(string.Format("{0}/{1}", vectors.Count, vectors.Distinct().Count()));
		foreach (var outputPath in outputPaths)
		{
			var dir = Path.Combine(outputPath, directoryName);
			Directory.CreateDirectory(dir);
			var fileName = Path.Combine(dir, animationClip.name.ToLower()) + ".clj";
			File.WriteAllText(fileName, writer.ToString());
			Debug.Log("Saved animation : " + Path.GetFullPath(fileName));
		}
	}

	delegate void Writer(string text);
	delegate int ResourceWriter(string text);

	static void WriteArray<T>(Writer writer, ResourceWriter resourceWriter, T[] array, Func<T, string> toString)
	{
		int i = resourceWriter(string.Format("[{0}]", (string.Join(" ", array.Select(toString).ToArray()))));
		writer(i.ToString() + " ");
	}

	static void WriteVector3(Writer writer, ResourceWriter resourceWriter, Vector3 vec)
	{
		WriteArray(writer, resourceWriter, new float[] { vec.x, vec.y, vec.z }, f => f.ToString("F3"));
	}

	static void WriteTransform(Writer writer, ResourceWriter resourceWriter, TransformState transform)
	{
		WriteVector3(writer, resourceWriter, transform.position);
		WriteVector3(writer, resourceWriter, transform.rotation);
		WriteVector3(writer, resourceWriter, transform.scale);
	}

	static void WriteLine(Writer writer, ResourceWriter resourceWriter, Line line)
	{
		writer("\n\t");
		writer(string.Format("\"{0}\"", line.name));
		writer(" [");
		foreach (var l in line.points)
		{
			writer(string.Format("\n\t\t[ {0} [ ", l.time));
			WriteTransform(writer, resourceWriter, l.transform);
			writer("] ]");
		}
		writer("\n\t]");
	}
	static void WriteAnimation(Writer writer, string name, float length, Line[] lines)
	{
		Dictionary<string, int> keys = new Dictionary<string, int>();
		List<string> values = new List<string>();
		ResourceWriter resourceWriter = r =>
		{
			int i;
			if (keys.TryGetValue(r, out i))
			{
				return i;
			}
			else
			{
				int c = values.Count;
				values.Add(r);
				return keys[r] = c;
			}
		};

		writer("{ " + string.Format(":name \"{0}\" :length {1} :bones", name, length) + " { ");
		foreach (var line in lines) WriteLine(writer, resourceWriter, line);
		writer(string.Format("\n\t:resources [\n\t\t{0}]", string.Join("\n\t\t", values.ToArray())));
		writer("\n} }");
	}
}
