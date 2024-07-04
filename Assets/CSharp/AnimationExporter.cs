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
	[SerializeField] AnimationClip animationClip;
	[SerializeField] string outputPath;

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
		return new Line(line.name, points);
	}

	static Bone TransformFrame(Matrix4x4 w2l, Transform transform, float time)
	{
		return new Bone(w2l * transform.localToWorldMatrix, transform.name, time);
	}
	static Bone[] AnimationFrame(GameObject root, AnimationClip clip, float time, Matrix4x4 w2l, Transform[] bones)
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

	[ContextMenu("Create animation")]
	void CreateAnimation ()
	{
		if (!EditorApplication.isPlaying)
		{
			Debug.Log("Enter play mode first");
			return;
		}
		
		StartCoroutine(CreateAnimation(gameObject, animationClip, outputPath));
	}
	static IEnumerator CreateAnimation(GameObject gameObject, AnimationClip animationClip, string outputPath)
	{
		var w2l = gameObject.transform.worldToLocalMatrix;
		var bones = Descendants(gameObject.transform).ToArray();
		var frameCount = (int)(animationClip.length * animationClip.frameRate);
		List<Bone[]> frames = new List<Bone[]>();

		foreach (var f in Enumerable.Range(0, frameCount))
		{
			var t = f / (float)frameCount;
			animationClip.SampleAnimation(gameObject, t);
			yield return new WaitForEndOfFrame();
			frames.Add(AnimationFrame(gameObject, animationClip, t, w2l, bones));
		}

		var lines = AnimationLines(frames).Select(CompressLine).ToArray();

		yield return new WaitForEndOfFrame();

		var writer = new StringBuilder();
		WriteAnimation(s => writer.Append(s), animationClip.name, animationClip.length, frameCount, lines);
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
		return f.Select(v => v.ToString()).ToArray();
	}

	static void WriteLine(Writer writer, Line line)
	{
		writer("\n\t{");
		writer(string.Format(":name \"{0}\"", line.name));
		writer("\n\t\t[");
		foreach (var l in line.points) writer(
			string.Format("\n\t\t\t[{0} [{1}]]",
				l.time,
				string.Join(" ", FloatToStringArray(MatrixToArray(l.matrix)))
			)
		);
		writer("\n\t\t]");
		writer("\n\t}");
	}
	static void WriteAnimation(Writer writer, string name, float length, int frameCount, Line[] lines)
	{
		writer("{ " + string.Format(":name \"{0}\" :length {1} :frames {2} :bones [ ", name, length, frameCount));
		foreach (var line in lines) WriteLine(writer, line);
		writer("\n] }");
	}
}
