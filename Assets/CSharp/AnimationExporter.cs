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
	[SerializeField] string outputPath;
	[SerializeField] GameObject defaultStance;

	class TransformState
	{
		public readonly Vector3 position;
		public readonly Quaternion rotation;

		public TransformState(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
		public static TransformState Create(Transform transform)
		{
			return new TransformState(transform.localPosition, transform.localRotation);
		}

		public Matrix4x4 LocalMatrix() { return Matrix4x4.TRS(position, rotation, Vector3.one); }
    }

	Dictionary<Transform, TransformState> states = new Dictionary<Transform, TransformState>();

	void Start()
	{
		var descendants = Descendants(transform);
		states = descendants.ToDictionary(t => t, t => new TransformState(t.localPosition, t.localRotation));
	}

	void ResetStates()
	{
		foreach (var d in Descendants(transform))
		{
			var t = states[d];
			d.localPosition = t.position;
			d.localRotation = t.rotation;
		}
	}

	static IEnumerable<Transform> Descendants(Transform transform)
	{
		return Enumerable.Range(0, transform.childCount).Select(transform.GetChild).SelectMany(t => new Transform[] { t }.Concat(Descendants(t)));
	}

	class Bone
	{
		public readonly Matrix4x4 matrix;
		public readonly string name;
		public readonly float time;

        public Bone(Matrix4x4 matrix, string name, float time)
        {
            this.matrix = matrix;
            this.name = name;
			this.time = time;
        }
    }

	class Point
	{
		public readonly float time;
		public readonly Matrix4x4 matrix;

        public Point(float time, Matrix4x4 matrix)
        {
            this.time = time;
            this.matrix = matrix;
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
			return x.matrix == y.matrix;
        }

        public int GetHashCode(Point obj)
        {
			return obj.matrix.GetHashCode();
        }
    }

	static Line CompressLine(Line line)
	{
		var points = line.points.Distinct(new PointComparer()).ToArray();
		if (points.Length == 1) return new Line(line.name, points);
		else return line;
	}

	static Bone TransformFrame(Func<Transform, Matrix4x4> source, Transform transform, float time)
	{
		var sm = source(transform);
		var nm = TransformState.Create(transform).LocalMatrix();

		return new Bone(sm.inverse * nm, transform.name, time);
	}
	static Bone[] AnimationFrame(float time, Func<Transform, Matrix4x4> w2l, Transform[] bones)
	{
		return bones.Select(b => TransformFrame(w2l, b, time)).ToArray();
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
			list.Add(new Point(bone.time, bone.matrix));
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
		
		StartCoroutine(CreateAnimations(gameObject, animationClips, outputPath));
	}

	IEnumerator CreateAnimations(GameObject gameObject, AnimationClip[] animationClips, string outputPath)
	{
		foreach (var animationClip in animationClips)
		{
			yield return StartCoroutine(CreateAnimation(gameObject, t => states[t].LocalMatrix(), animationClip, outputPath));
			ResetStates();
			yield return new WaitForEndOfFrame();
		}
	}

	static IEnumerator CreateAnimation(GameObject gameObject, Func<Transform, Matrix4x4> w2l, AnimationClip animationClip, string outputPath)
	{
		var bones = Descendants(gameObject.transform).ToArray();
		var frameCount = (int)(animationClip.length * animationClip.frameRate);
		if (animationClip.length == 0) frameCount = 1;

		List<Bone[]> frames = new List<Bone[]>();

		foreach (var f in Enumerable.Range(0, frameCount))
		{
			var t = f / (float)frameCount;
			animationClip.SampleAnimation(gameObject, t);
			yield return new WaitForEndOfFrame();
			frames.Add(AnimationFrame(t, w2l, bones));
		}

		var lines = AnimationLines(frames).Select(CompressLine).ToArray();

		yield return new WaitForEndOfFrame();

		var writer = new StringBuilder();
		WriteAnimation(s => writer.Append(s), animationClip.name, animationClip.length, lines);
		Directory.CreateDirectory(outputPath);
		var fileName = Path.Combine(outputPath, animationClip.name.ToLower()) + ".clj";
		File.WriteAllText(fileName, writer.ToString());
		Debug.Log("Saved animation : " + Path.GetFullPath(fileName));
	}

	delegate void Writer(string text);

	static float[] MatrixToArray(Matrix4x4 m)
	{
		return Enumerable.Range(0, 16).Select(i => m[i]).ToArray();
	}
	static string[] FloatToStringArray(float[] f)
	{
		return f.Select(v => v.ToString("F3")).ToArray();
	}

	static void WriteLine(Writer writer, Line line)
	{
		writer("\n\t");
		writer(string.Format("\"{0}\"", line.name));
		writer(" [");
		foreach (var l in line.points) writer(
			string.Format("\n\t\t[{0} [{1}]]",
				l.time,
				string.Join(" ", FloatToStringArray(MatrixToArray(l.matrix)))
			)
		);
		writer("\n\t]");
	}
	static void WriteAnimation(Writer writer, string name, float length, Line[] lines)
	{
		writer("{ " + string.Format(":name \"{0}\" :length {1} :bones", name, length) + " { ");
		foreach (var line in lines) WriteLine(writer, line);
		writer("\n} }");
	}
}
