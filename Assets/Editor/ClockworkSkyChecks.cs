using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WonderlandFlight;

public static class ClockworkSkyChecks
{
    [MenuItem("Clockwork Sky/Run Smoke Checks")]
    public static void Run()
    {
        var host = new GameObject("Clockwork Sky checks");
        var game = host.AddComponent<ClockworkSkyGame>();
        var pieces = new List<GameObject>();
        var stars = (List<GameObject>)Field(game, "stars");
        var randomState = UnityEngine.Random.state;
        Sprite sprite = null;
        try
        {
            Call(game, "CreateRuntimeResources");
            sprite = (Sprite)Field(game, "squareSprite");
            foreach (Transform parent in new[] { null, host.transform })
            {
                var piece = (GameObject)Call(game, "MakePiece", "Check piece", new Vector2(2, 3), new Vector2(4, 5), Color.red, 7, parent);
                pieces.Add(piece);
                Require(piece.transform.parent == parent && piece.transform.localPosition == new Vector3(2, 3, 0), "piece position and parent");
                Require(piece.transform.localScale == new Vector3(4, 5, 1), "piece scale");
                var renderer = piece.GetComponent<SpriteRenderer>();
                Require(renderer.sprite == sprite && renderer.color == Color.red && renderer.sortingOrder == 7, "piece appearance");
            }
            float[] angles = { 0, 90, -90, 180 };
            Vector2[] expected = { Vector2.right, Vector2.up, Vector2.down, Vector2.left };
            for (int i = 0; i < angles.Length; i++)
                Require(Vector2.Distance((Vector2)Call(game, "Rotate", Vector2.right, angles[i]), expected[i]) < .00001f, "rotation " + angles[i]);
            Call(game, "CreateStars");
            Require(stars.Count == 70, "star count");
            foreach (GameObject star in stars)
            {
                var renderer = star.GetComponent<SpriteRenderer>();
                Require(renderer.sprite == sprite && renderer.color.a >= .25f && renderer.color.a <= .85f, "star appearance");
            }
            Call(game, "ResetRunState", true);
            Require((int)Field(game, "lives") == 5 && (int)Field(game, "powerCursor") == -1, "garden reset");
            Call(game, "ReturnToTitle");
            Require(Field(game, "state").ToString() == "Title", "return to title");
            Call(game, "ResetRunState", false);
            Require((int)Field(game, "lives") == 3, "story reset");
            Debug.Log("Clockwork Sky smoke checks passed.");
        }
        finally
        {
            foreach (GameObject piece in pieces) UnityEngine.Object.DestroyImmediate(piece);
            foreach (GameObject star in stars) UnityEngine.Object.DestroyImmediate(star);
            if (sprite != null) { UnityEngine.Object.DestroyImmediate(sprite.texture); UnityEngine.Object.DestroyImmediate(sprite); }
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Random.state = randomState;
        }
    }

    private static object Call(object game, string name, params object[] arguments) => typeof(ClockworkSkyGame).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic).Invoke(game, arguments);
    private static object Field(object game, string name) => typeof(ClockworkSkyGame).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Clockwork Sky check failed: " + message); }
}
