using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class AnimationExporter : MonoBehaviour
{
	[SerializeField] AnimationClip animationClip;
	[SerializeField] string outputPath;

	static IEnumerable<Transform> Descendants(Transform transform)
	{
		return Enumerable.Range(0, transform.childCount).Select(transform.GetChild).SelectMany(t => new Transform[] { t }.Concat(Descendants(t)));
	}

	struct Bone
	{
		public readonly Matrix4x4 matrix;
		public readonly string name;

        public Bone(Matrix4x4 matrix, string name)
        {
            this.matrix = matrix;
            this.name = name;
        }
    }

	struct Line
	{
		public readonly string name;
		public readonly Matrix4x4[] matrices;

        public Line(string name, Matrix4x4[] matrices)
        {
            this.name = name;
            this.matrices = matrices;
        }
    }

	static Bone TransformFrame(Matrix4x4 w2l, Transform transform)
	{
		return new Bone(w2l * transform.localToWorldMatrix, transform.name);
	}
	static Bone[] AnimationFrame(GameObject root, AnimationClip clip, float time, Matrix4x4 w2l, Transform[] bones)
	{
		clip.SampleAnimation(root, time);
		return bones.Select(b => TransformFrame(w2l, b)).ToArray();
	}
	static Line[] AnimationLines(List<Bone[]> frames)
	{
		if (frames.Count == 0) return new Line[0];
		var map = new Dictionary<string, List<Matrix4x4>>();
		foreach (var bone in frames.SelectMany(f => f))
		{
			List<Matrix4x4> list;
			if (!map.TryGetValue(bone.name, out list))
			{
				list = new List<Matrix4x4>();
				map[bone.name] = list;
			}
			list.Add(bone.matrix);
		}
		return map.Select(p => new Line(p.Key, p.Value.ToArray())).ToArray();
	}

	[ContextMenu("Create animation")]
	void CreateAnimation ()
	{
		var w2l = gameObject.transform.worldToLocalMatrix;
		var bones = Descendants(gameObject.transform).ToArray();
		var frameCount = (int)(animationClip.length * animationClip.frameRate);
        var frames = Enumerable.Range(0, frameCount).Select(f => AnimationFrame(gameObject, animationClip, f / frameCount, w2l, bones)).ToList();
		var lines = AnimationLines(frames);
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
		foreach (var m in line.matrices.Select(MatrixToArray).Select(FloatToStringArray)) writer(string.Format("\n\t\t\t[{0}]", string.Join(" ", m)));
		writer("\n\t\t]");
		writer("\n\t}");
	}
	static void WriteAnimation(Writer writer, string name, float length, int frameCount, Line[] lines)
	{
		writer("{ " + string.Format(":name \"{0}\" :length {1} :frames {2} :bones [ ", name, length, frameCount));
		foreach (var line in lines) WriteLine(writer, line);
		writer("\n] }");
	}

	void Update ()
	{
		var t = Time.time;
		if (animationClip.isLooping) t = Mathf.Repeat(t, animationClip.length);
        animationClip.SampleAnimation(gameObject, t);
	}
}
