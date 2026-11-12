using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-off editor tool: moves the therapy session from the nature scene into the
// "Low Poly Recording Studio" room.
//
//   Tools > Music Therapy > Migrate ActualScene to Recording Studio
//
// What it does (all in ActualScene, nothing is deleted outright):
//   1. Makes a backup copy of ActualScene (ActualScene_NatureBackup.unity) the first time.
//   2. Brings the studio room from the pack's Demo scene in — without the people, the demo
//      camera/post-processing and the "Instrument Skins" showcase outside the room.
//   3. Turns the nature off (Environment, nature ambience zone, the camp↔sequencer portals,
//      the outdoor directional light) — deactivated, not deleted.
//   4. Clears the studio's own props out of the spots the therapy stations need, and removes
//      the studio's decorative drums/pianos/synth/sticks so patients don't try to play props.
//      Everything removed is parked, inactive, under "_Removed Studio Props".
//   5. Places the stations in the live room around the player's start point: drum kit,
//      piano, percussion cabinet (bongo + maracas), sequencer, Lüscher test UI, plus a
//      teleport anchor in front of each station. Real renderer bounds are used, so objects
//      sit on the floor and are only scaled DOWN if they don't fit their spot.
//   6. Makes the sequencer more compact (spacing, button size, glow, Record button).
//   7. Switches lighting to the studio's (flat ambient, studio lights, no fog).
//
// The scene is left modified but NOT saved — look around, then Ctrl+S. To start over,
// open ActualScene_NatureBackup.unity, "Save As" ActualScene.unity and run the tool again.
public static class StudioSceneMigration
{
    private const string ActualScenePath = "Assets/SimpleLowPolyNature/Scenes/ActualScene.unity";
    private const string BackupScenePath = "Assets/SimpleLowPolyNature/Scenes/ActualScene_NatureBackup.unity";
    private const string StudioDemoScenePath = "Assets/Low Poly Recording Studio/Scenes/Demo.unity";
    private const string StudioPrefabFolder = "Assets/Low Poly Recording Studio/Prefabs/";
    private const string SeqButtonPrefabPath = "Assets/Prefab/SeqButton.prefab";

    private const string StudioRootName = "Studio Environment";
    private const string LayoutRootName = "Therapy Layout";
    private const string RemovedRootName = "_Removed Studio Props";

    // --- Studio layout (world coordinates of the pack's Demo scene, metres) -------------
    // The room: control room on the west (x < -9.3, behind the glass), live room on the
    // east. Everything therapy-related goes in the live room. If the room is ever moved,
    // all of these are shifted by however far its floor moved (see roomOffset).
    private static readonly Vector2 ExpectedFloorMin = new Vector2(-15.604f, -7.942f);

    private static readonly Vector2 Spawn = new Vector2(-5.15f, -3.0f);        // player start, facing +Z
    private static readonly Vector2 DrumsSpot = new Vector2(-6.85f, -0.9f);
    private static readonly Vector2 DrumsMaxFootprint = new Vector2(2.2f, 2.0f);
    private static readonly Vector2 PianoSpot = new Vector2(-3.35f, -0.9f);
    private static readonly Vector2 PianoMaxFootprint = new Vector2(2.0f, 1.4f);
    private static readonly Vector2 PercussionSpot = new Vector2(-7.75f, -3.3f); // cabinet with bongo + maracas
    private static readonly Vector3 SequencerAnchorPos = new Vector3(-2.35f, 1.25f, -3.3f);
    private const float SequencerAnchorYaw = 90f;                               // forward = +X, player stands on the -X side
    private static readonly Vector3 SurveyCanvasPos = new Vector3(-5.15f, 1.45f, -1.75f);
    private const float SurveyMaxWidth = 1.5f;
    private static readonly Vector2 ExtrasStart = new Vector2(-8.5f, -6.2f);    // anything unexpected in "Instruments"

    // Areas (centre, size) kept free of studio props.
    private static readonly (Vector2 center, Vector2 size)[] ClearZones =
    {
        (Spawn, new Vector2(2.2f, 2.2f)),
        (DrumsSpot, new Vector2(2.6f, 2.4f)),
        (PianoSpot, new Vector2(2.4f, 1.8f)),
        (PercussionSpot, new Vector2(1.2f, 2.2f)),
        (new Vector2(-2.55f, -3.3f), new Vector2(1.6f, 1.8f)),   // sequencer + the spot to stand at it
        (new Vector2(-5.15f, -1.75f), new Vector2(1.9f, 0.7f)),  // Lüscher test canvas
        (new Vector2(-5.15f, -1.6f), new Vector2(1.0f, 3.0f)),   // walkway between start and front stations
    };

    // Studio pieces that are part of the room itself — never cleared.
    private static readonly HashSet<string> StructuralPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Floor", "Ceiling", "Wall Brick", "Wall Gray", "Wall Red", "Wall Window", "Door",
        "Carpet Black", "Carpet Brown", "Lamp", "Lamp A", "Lamp B", "Lamp Bar", "Lamp Wall Decor",
        "Acoustic Absorb Panel", "Exit Sign", "Logo", "Not Disturb Sign", "Painting",
    };

    // Studio props that look playable but aren't — removed everywhere so patients don't
    // reach for them instead of the real instruments.
    private static readonly HashSet<string> FakeInstrumentPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Drums", "Piano", "Piano White", "Synth", "Drum Sticks", "Drum Stick One",
    };

    private static readonly string[] SkippedDemoRoots = { "Camera", "Camera And Post Processing", "Characters", "Instrument Skins" };

    // --- Sequencer: more compact layout -------------------------------------------------
    private const float SeqButtonSpacing = 0.12f;   // was 0.16
    private const float SeqRowSpacing = 0.12f;      // was 0.15
    private const float SeqGroupGap = 0.08f;        // was 0.14
    private const float SeqHeaderHeight = 0.09f;    // was 0.12
    private const float SeqHeaderScale = 0.015f;    // was 0.02
    private const float SeqRecordButtonGap = 0.09f; // was 0.11
    private const float SeqButtonScale = 0.22f;     // SeqButton prefab root, was 0.3
    private const float SeqGlowRadius = 0.12f;      // was 0.3 (a 60 cm glow per button)
    private const float SeqRecordCanvasScale = 0.008f; // was 0.01

    private static readonly StringBuilder Report = new StringBuilder();

    [MenuItem("Tools/Music Therapy/Migrate ActualScene to Recording Studio")]
    public static void Migrate()
    {
        if (!File.Exists(StudioDemoScenePath))
        {
            EditorUtility.DisplayDialog("Studio migration", $"Studio scene not found: {StudioDemoScenePath}\nImport the Low Poly Recording Studio package first.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Studio migration",
                "ActualScene will be moved into the recording studio room.\n\n" +
                "A backup (ActualScene_NatureBackup.unity) is made first. The scene is NOT saved automatically — check it, then press Ctrl+S.\n\nContinue?",
                "Migrate", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Report.Clear();

        if (!File.Exists(BackupScenePath))
        {
            AssetDatabase.CopyAsset(ActualScenePath, BackupScenePath);
            Log($"Backup created: {BackupScenePath}");
        }
        else
        {
            Log($"Backup already exists, kept as is: {BackupScenePath}");
        }

        Scene scene = EditorSceneManager.OpenScene(ActualScenePath, OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(go => go.name == StudioRootName))
        {
            EditorUtility.DisplayDialog("Studio migration",
                "This scene has already been migrated (\"Studio Environment\" exists).\n\nTo run again, restore ActualScene from ActualScene_NatureBackup.unity first.", "OK");
            return;
        }

        try
        {
            Run(scene);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Studio migration", "Migration stopped with an error (see Console). The scene was not saved — reopen ActualScene without saving to discard.", "OK");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[StudioSceneMigration]\n" + Report);
        EditorUtility.DisplayDialog("Studio migration",
            "Done. The full list of changes is in the Console.\n\nLook around the live room (Scene view), adjust anything you like, then save with Ctrl+S.", "OK");
    }

    // ===================================================================== main flow

    private static void Run(Scene scene)
    {
        // ---- 1. Find the therapy objects (before anything moves) -----------------------
        GameObject environment = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Environment");
        GameObject instruments = FindPrefabInstanceRoot(scene, "Assets/Prefab/Instruments.prefab");
        GameObject ui = FindPrefabInstanceRoot(scene, "Assets/Prefab/UI.prefab");
        GameObject teleportArea = FindPrefabInstanceRoot(scene, "Assets/Prefab/TeleportArea.prefab");
        GameObject xrOrigin = FindComponentByTypeName(scene, "XROrigin")?.gameObject;
        if (xrOrigin != null) xrOrigin = PrefabUtility.GetOutermostPrefabInstanceRoot(xrOrigin) ?? xrOrigin;
        SequencerPanelBuilder sequencer = UnityEngine.Object.FindObjectsByType<SequencerPanelBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

        Canvas surveyCanvas = ui != null
            ? ui.GetComponentsInChildren<Canvas>(true).FirstOrDefault(c => c.name == "Survey") ?? ui.GetComponentsInChildren<Canvas>(true).FirstOrDefault()
            : null;

        Log($"Found: Instruments={Name(instruments)}, UI={Name(ui)}, Survey canvas={Name(surveyCanvas)}, XR Origin={Name(xrOrigin)}, " +
            $"TeleportArea={Name(teleportArea)}, Sequencer={Name(sequencer)}, Environment={Name(environment)}");

        // Where the player used to stand in front of the instruments (the camp): in front of
        // the Lüscher canvas. Used to keep each instrument's "front" facing the player.
        Vector3 oldCampViewer = surveyCanvas != null
            ? surveyCanvas.transform.position - Flat(surveyCanvas.transform.forward).normalized * 2f
            : xrOrigin != null ? xrOrigin.transform.position : Vector3.zero;

        var groups = new List<Station>();
        if (instruments != null)
        {
            foreach (Transform child in instruments.transform)
            {
                if (!child.gameObject.activeSelf || !TryGetBounds(child.gameObject, out Bounds b)) continue;
                groups.Add(new Station
                {
                    Root = child,
                    Kind = ClassifyInstrument(child.name),
                    OldCenter = b.center,
                    OldElevation = MeasureElevation(child.gameObject, b, environment),
                });
            }
        }

        // ---- 2. Studio room in, nature out ------------------------------------------------
        GameObject studio = ImportStudio(scene);
        var layout = new GameObject(LayoutRootName);
        SceneManager.MoveGameObjectToScene(layout, scene);
        var removed = new GameObject(RemovedRootName);
        SceneManager.MoveGameObjectToScene(removed, scene);

        DisableNature(scene, environment, studio);

        // ---- 3. Room position check ---------------------------------------------------------
        Vector2 roomOffset = Vector2.zero;
        float floorY = 0f;
        if (TryGetFloorBounds(studio, out Bounds floor))
        {
            roomOffset = new Vector2(floor.min.x, floor.min.z) - ExpectedFloorMin;
            floorY = floor.max.y;
            Log($"Studio floor: {floor.min.x:0.##}..{floor.max.x:0.##} x {floor.min.z:0.##}..{floor.max.z:0.##}, top at y={floorY:0.###}, offset from expected {roomOffset}");
        }
        else
        {
            Log("WARNING: studio floor not found — using the pack's default coordinates.");
        }
        Vector2 P(Vector2 v) => v + roomOffset;
        Vector3 P3(Vector3 v) => new Vector3(v.x + roomOffset.x, v.y + floorY, v.z + roomOffset.y);

        // ---- 4. Make room for the stations ----------------------------------------------------
        RemoveFakeInstruments(studio, removed.transform);
        ClearZonesOfProps(studio, removed.transform, roomOffset);
        removed.SetActive(false);

        Vector3 spawn = new Vector3(P(Spawn).x, floorY, P(Spawn).y);

        // ---- 5. Stations --------------------------------------------------------------------
        var teleportTargets = new List<(string name, Vector3 standAt, Vector3 lookAt)>();

        foreach (Station s in groups.Where(g => g.Kind == StationKind.Drums))
        {
            PlaceOnFloor(s, P(DrumsSpot), DrumsMaxFootprint, floorY, oldCampViewer, spawn);
            teleportTargets.Add(("Drums", StandPoint(s.Root.gameObject, spawn, 0.45f), BoundsCenter(s.Root.gameObject)));
        }

        foreach (Station s in groups.Where(g => g.Kind == StationKind.Piano))
        {
            PlaceOnFloor(s, P(PianoSpot), PianoMaxFootprint, floorY, oldCampViewer, spawn);
            teleportTargets.Add(("Piano", StandPoint(s.Root.gameObject, spawn, 0.35f), BoundsCenter(s.Root.gameObject)));
        }

        var percussion = groups.Where(g => g.Kind == StationKind.Percussion).ToList();
        if (percussion.Count > 0)
        {
            GameObject cabinet = PlacePercussionCabinet(scene, layout.transform, P(PercussionSpot), floorY, spawn, percussion, oldCampViewer);
            if (cabinet != null)
                teleportTargets.Add(("Percussion", StandPoint(cabinet, spawn, 0.35f), BoundsCenter(cabinet)));
        }

        PlaceExtras(groups.Where(g => g.Kind == StationKind.Other).ToList(), P(ExtrasStart), floorY, oldCampViewer, spawn);

        if (surveyCanvas != null)
            PlaceSurvey(ui.transform, surveyCanvas, P3(SurveyCanvasPos), spawn);

        if (sequencer != null)
        {
            Vector3 anchorPos = P3(SequencerAnchorPos);
            MakeSequencerCompact(sequencer, anchorPos, SequencerAnchorYaw);
            Vector3 standAt = anchorPos - Quaternion.Euler(0, SequencerAnchorYaw, 0) * Vector3.forward * 0.55f;
            teleportTargets.Add(("Sequencer", new Vector3(standAt.x, floorY, standAt.z), anchorPos));
        }

        // ---- 6. Player + teleport anchors --------------------------------------------------
        if (xrOrigin != null)
        {
            xrOrigin.transform.SetPositionAndRotation(spawn, Quaternion.identity);
            Log($"XR Origin moved to start point {spawn} facing +Z (towards drums, piano and the test).");
            foreach (FallResetPos fall in xrOrigin.GetComponentsInChildren<FallResetPos>(true))
            {
                var so = new SerializedObject(fall);
                so.FindProperty("startingPos").vector3Value = spawn + Vector3.up * 0.05f;
                so.ApplyModifiedPropertiesWithoutUndo();
                Log("FallResetPos.startingPos set to the start point.");
            }
        }

        teleportTargets.Insert(0, ("Start", spawn, spawn + Vector3.forward));
        PlaceTeleportAnchors(scene, teleportArea, layout.transform, teleportTargets);
        SetUpLiveRoomTeleportFloor(teleportArea, layout.transform, roomOffset, floorY);

        // ---- 7. Lighting -----------------------------------------------------------------------
        ApplyStudioLighting(studio);

        Selection.activeGameObject = layout;
    }

    // ===================================================================== studio import

    private static GameObject ImportStudio(Scene scene)
    {
        Scene demo = EditorSceneManager.OpenScene(StudioDemoScenePath, OpenSceneMode.Additive);
        var studio = new GameObject(StudioRootName);
        SceneManager.MoveGameObjectToScene(studio, scene);

        int moved = 0;
        foreach (GameObject root in demo.GetRootGameObjects())
        {
            if (SkippedDemoRoots.Contains(root.name))
            {
                Log($"Studio: skipped \"{root.name}\" (people / demo camera / showcase outside the room).");
                continue;
            }
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetParent(studio.transform, true);
            moved++;
        }
        EditorSceneManager.CloseScene(demo, true);
        Log($"Studio: {moved} groups brought in from the pack's Demo scene.");

        // People can also sit elsewhere in the hierarchy (e.g. the technician in the control room).
        int people = 0;
        foreach (Transform t in studio.GetComponentsInChildren<Transform>(true).ToList())
        {
            if (t == null) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) ?? "";
            if (path.Contains("/Prefabs/Characters/"))
            {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject) ?? t.gameObject;
                UnityEngine.Object.DestroyImmediate(root);
                people++;
            }
        }
        if (people > 0) Log($"Studio: removed {people} more character(s).");

        return studio;
    }

    private static void DisableNature(Scene scene, GameObject environment, GameObject studio)
    {
        if (environment != null)
        {
            environment.SetActive(false);
            Log("Nature: \"Environment\" (terrain, trees, water, camp) deactivated.");
        }

        foreach (AreaAudioTrigger zone in UnityEngine.Object.FindObjectsByType<AreaAudioTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            zone.gameObject.SetActive(false);
            Log($"Nature: ambience zone \"{zone.name}\" deactivated.");
        }

        foreach (Teleport portal in UnityEngine.Object.FindObjectsByType<Teleport>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            portal.gameObject.SetActive(false);
            Log($"Nature: camp portal \"{portal.name}\" deactivated (everything is in one room now).");
        }

        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional && !light.transform.IsChildOf(studio.transform) && light.gameObject.scene == scene)
            {
                light.gameObject.SetActive(false);
                Log($"Lighting: outdoor light \"{light.name}\" deactivated.");
            }
        }
    }

    private static void RemoveFakeInstruments(GameObject studio, Transform removed)
    {
        foreach (GameObject root in StudioPrefabRoots(studio))
        {
            if (FakeInstrumentPrefabs.Contains(PrefabName(root)))
            {
                root.transform.SetParent(removed, true);
                Log($"Studio: decorative \"{root.name}\" parked (looks playable but isn't).");
            }
        }
    }

    private static void ClearZonesOfProps(GameObject studio, Transform removed, Vector2 offset)
    {
        var zones = ClearZones.Select(z => new Rect(z.center + offset - z.size / 2f, z.size)).ToList();
        int count = 0;
        foreach (GameObject root in StudioPrefabRoots(studio))
        {
            if (StructuralPrefabs.Contains(PrefabName(root))) continue;
            if (!TryGetBounds(root, out Bounds b) || b.min.y > 1.8f) continue; // wall-mounted / high up

            var footprint = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            if (zones.Any(z => z.Overlaps(footprint)))
            {
                root.transform.SetParent(removed, true);
                count++;
            }
        }
        Log($"Studio: {count} props moved out of the way of the therapy stations (parked under \"{RemovedRootName}\").");
    }

    // ===================================================================== stations

    private enum StationKind { Drums, Piano, Percussion, Other }

    private sealed class Station
    {
        public Transform Root;
        public StationKind Kind;
        public Vector3 OldCenter;
        public float OldElevation;
    }

    private static StationKind ClassifyInstrument(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("drum_kit") || n == "drums") return StationKind.Drums;
        if (n.Contains("piano") || n.Contains("keyb")) return StationKind.Piano;
        if (n.Contains("bongo") || n.Contains("macaras") || n.Contains("maracas")) return StationKind.Percussion;
        return StationKind.Other;
    }

    // Turns the object so it keeps facing the player the way it faced the player at the camp,
    // shrinks it if it doesn't fit, and stands it on the floor at `spot` (keeping the height it
    // had above the old ground, e.g. a keyboard on its own stand).
    private static void PlaceOnFloor(Station s, Vector2 spot, Vector2 maxFootprint, float floorY, Vector3 oldViewer, Vector3 newViewer)
    {
        Transform t = s.Root;
        Vector3 target = new Vector3(spot.x, floorY, spot.y);
        KeepFacing(t, s.OldCenter, oldViewer, target, newViewer);
        float scaled = FitFootprint(t, maxFootprint);
        MoveBoundsTo(t, spot, floorY + Mathf.Clamp(s.OldElevation, 0f, 1.2f));
        Log($"Station: \"{t.name}\" → ({spot.x:0.##}, {spot.y:0.##})" +
            (s.OldElevation > 0.02f ? $", {s.OldElevation:0.##} m above the floor as before" : "") +
            (scaled < 0.999f ? $", scaled ×{scaled:0.##} to fit" : ""));
    }

    // Bongo and maracas share a cabinet (≈0.87 m top) next to the player's start point, so
    // they're at a comfortable standing height in VR.
    private static GameObject PlacePercussionCabinet(Scene scene, Transform parent, Vector2 spot, float floorY, Vector3 viewer,
        List<Station> items, Vector3 oldViewer)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(StudioPrefabFolder + "Cabinet.prefab");
        if (asset == null)
        {
            Log("WARNING: Cabinet prefab not found — percussion placed on the floor instead.");
            for (int i = 0; i < items.Count; i++)
                PlaceOnFloor(items[i], spot + new Vector2(0, (i - (items.Count - 1) / 2f) * 0.8f), new Vector2(0.8f, 0.8f), floorY, oldViewer, viewer);
            return null;
        }

        var cabinet = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        cabinet.name = "Percussion Cabinet";
        cabinet.transform.SetParent(parent, true);
        Vector3 toViewer = Flat(viewer - new Vector3(spot.x, floorY, spot.y)).normalized;
        cabinet.transform.rotation = Quaternion.LookRotation(toViewer, Vector3.up); // long side faces the player
        MoveBoundsTo(cabinet.transform, spot, floorY);
        TryGetBounds(cabinet, out Bounds top);

        // Lay the items out along the cabinet's long axis (its local X).
        Vector3 along = cabinet.transform.right;
        float usable = Mathf.Max(0.4f, Mathf.Max(top.size.x, top.size.z) * 0.9f); // long side of the top
        float slot = usable / Mathf.Max(1, items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            Station s = items[i];
            Vector3 c3 = new Vector3(spot.x, 0, spot.y) + along * ((i - (items.Count - 1) / 2f) * slot);
            KeepFacing(s.Root, s.OldCenter, oldViewer, new Vector3(c3.x, top.max.y, c3.z), viewer);
            float scaled = FitFootprint(s.Root, new Vector2(slot * 0.95f, 0.5f));
            MoveBoundsTo(s.Root, new Vector2(c3.x, c3.z), top.max.y + 0.005f);
            Log($"Station: \"{s.Root.name}\" on the percussion cabinet" + (scaled < 0.999f ? $", scaled ×{scaled:0.##} to fit" : ""));
        }
        return cabinet;
    }

    private static void PlaceExtras(List<Station> extras, Vector2 start, float floorY, Vector3 oldViewer, Vector3 viewer)
    {
        for (int i = 0; i < extras.Count; i++)
        {
            Vector2 spot = start + new Vector2(i * 1.0f, 0);
            PlaceOnFloor(extras[i], spot, new Vector2(0.9f, 0.9f), floorY, oldViewer, viewer);
            Log($"NOTE: \"{extras[i].Root.name}\" wasn't a known instrument — put by the south wall, check it.");
        }
    }

    // Moves the whole UI prefab so the Lüscher canvas floats upright in front of the start
    // point, facing the player, no wider than SurveyMaxWidth.
    private static void PlaceSurvey(Transform uiRoot, Canvas canvas, Vector3 canvasPos, Vector3 viewer)
    {
        Transform c = canvas.transform;
        Quaternion wanted = Quaternion.LookRotation(Flat(canvasPos - viewer).normalized, Vector3.up); // viewer looks along +forward
        Quaternion delta = wanted * Quaternion.Inverse(c.rotation);
        uiRoot.rotation = delta * uiRoot.rotation;
        uiRoot.position += canvasPos - c.position;

        var rect = c as RectTransform;
        if (rect != null)
        {
            float width = rect.rect.width * c.lossyScale.x;
            if (width > SurveyMaxWidth)
            {
                float k = SurveyMaxWidth / width;
                c.localScale *= k;
                Log($"UI: Lüscher canvas scaled ×{k:0.##} (was {width:0.##} m wide).");
            }
        }
        uiRoot.position += canvasPos - c.position; // re-centre after scaling
        Log($"UI: Lüscher test canvas in front of the start point at {canvasPos}.");
    }

    private static void MakeSequencerCompact(SequencerPanelBuilder builder, Vector3 anchorPos, float anchorYaw)
    {
        var so = new SerializedObject(builder);
        void Set(string field, float value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null) p.floatValue = value;
        }
        Set("buttonSpacing", SeqButtonSpacing);
        Set("rowSpacing", SeqRowSpacing);
        Set("groupGap", SeqGroupGap);
        Set("headerHeight", SeqHeaderHeight);
        Set("headerScale", SeqHeaderScale);
        Set("recordButtonGap", SeqRecordButtonGap);
        so.ApplyModifiedPropertiesWithoutUndo();

        var anchor = so.FindProperty("physicalButtonsRoot").objectReferenceValue as Transform;
        if (anchor != null)
        {
            anchor.SetPositionAndRotation(anchorPos, Quaternion.Euler(0, anchorYaw, 0));
            Log($"Sequencer: panel anchor moved to {anchorPos}, facing the room.");
        }

        var record = so.FindProperty("recordButtonRoot").objectReferenceValue as Transform;
        if (record != null)
        {
            record.localScale = Vector3.one * SeqRecordCanvasScale;
            if (anchor != null) record.SetPositionAndRotation(anchorPos, anchor.rotation); // re-placed under the panel at runtime anyway
        }

        // The button prefab itself: smaller buttons and a much smaller glow.
        GameObject contents = PrefabUtility.LoadPrefabContents(SeqButtonPrefabPath);
        try
        {
            contents.transform.localScale = Vector3.one * SeqButtonScale;
            foreach (SequencerButton button in contents.GetComponentsInChildren<SequencerButton>(true))
            {
                var bso = new SerializedObject(button);
                SerializedProperty glow = bso.FindProperty("glowRadius");
                if (glow != null) glow.floatValue = SeqGlowRadius;
                bso.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(contents, SeqButtonPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        Log($"Sequencer: compact layout — button spacing {SeqButtonSpacing} m, rows {SeqRowSpacing} m, group gap {SeqGroupGap} m, " +
            $"buttons ×{SeqButtonScale / 0.3f:0.##}, glow {SeqGlowRadius} m, Record button ×{SeqRecordCanvasScale / 0.01f:0.##}. " +
            "With the 8-sound bank the panel is about 0.8 m wide instead of ~1.1 m (+ glow).");
    }

    // Re-uses the existing teleport anchors (and adds more from the same prefab if needed):
    // one at the start point and one in front of each station, facing it.
    private static void PlaceTeleportAnchors(Scene scene, GameObject teleportArea, Transform layout, List<(string name, Vector3 standAt, Vector3 lookAt)> targets)
    {
        // Whole "Teleport Anchor" prefab instances (snap volume + anchor + visuals), not just
        // the GameObject that carries the TeleportationAnchor component.
        const string anchorPrefabPath = "Assets/Prefab/Teleport Anchor.prefab";
        var anchors = new List<GameObject>();
        if (teleportArea != null)
        {
            foreach (Transform child in teleportArea.transform)
            {
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject) == anchorPrefabPath)
                    anchors.Add(child.gameObject);
            }
        }

        var anchorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(anchorPrefabPath);
        while (anchors.Count < targets.Count && anchorAsset != null)
        {
            var extra = (GameObject)PrefabUtility.InstantiatePrefab(anchorAsset, scene);
            extra.transform.SetParent(teleportArea != null ? teleportArea.transform : layout, true);
            anchors.Add(extra);
        }

        for (int i = 0; i < anchors.Count; i++)
        {
            if (i >= targets.Count)
            {
                anchors[i].SetActive(false);
                continue;
            }
            var (name, standAt, lookAt) = targets[i];
            Vector3 look = Flat(lookAt - standAt);
            Quaternion rot = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity;
            anchors[i].transform.SetPositionAndRotation(standAt, rot);
            anchors[i].name = $"Teleport Anchor - {name}";
            anchors[i].SetActive(true);
        }
        Log($"Teleport: {Math.Min(anchors.Count, targets.Count)} anchors — {string.Join(", ", targets.Select(t => t.name))}.");
    }

    // The old TeleportationArea pointed at the (now disabled) terrain. Give it a thin box over
    // the live-room floor instead, so the patient can also teleport freely inside the room.
    private static readonly Rect LiveRoomFloor = Rect.MinMaxRect(-9.1f, -7.75f, -1.15f, 3.45f);

    private static void SetUpLiveRoomTeleportFloor(GameObject teleportArea, Transform layout, Vector2 offset, float floorY)
    {
        Component area = teleportArea != null
            ? teleportArea.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "TeleportationArea")
            : null;
        if (area == null)
        {
            Log("Teleport: no TeleportationArea found — only the anchors can be used.");
            return;
        }

        var floorGo = new GameObject("Live Room Teleport Floor");
        floorGo.transform.SetParent(layout, false);
        var box = floorGo.AddComponent<BoxCollider>();
        Rect r = new Rect(LiveRoomFloor.position + offset, LiveRoomFloor.size);
        floorGo.transform.position = new Vector3(r.center.x, floorY + 0.005f, r.center.y);
        box.size = new Vector3(r.width, 0.01f, r.height);

        var so = new SerializedObject(area);
        SerializedProperty colliders = so.FindProperty("m_Colliders");
        if (colliders != null)
        {
            colliders.arraySize = 1;
            colliders.GetArrayElementAtIndex(0).objectReferenceValue = box;
            so.ApplyModifiedPropertiesWithoutUndo();
            Log($"Teleport: free teleport over the live-room floor ({r.width:0.#} × {r.height:0.#} m).");
        }
    }

    private static void ApplyStudioLighting(GameObject studio)
    {
        // Same look as the pack's demo: flat grey ambient, no fog (no sky is visible indoors).
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.387f, 0.387f, 0.387f);
        RenderSettings.fog = false;

        // The demo uses six directional lights; on Quest every extra per-pixel light costs a
        // full extra pass. Keep the main one per-pixel (with hard shadows) and make the fill
        // lights cheap vertex lights — visually nearly identical in a flat-shaded low-poly room.
        foreach (Light light in studio.GetComponentsInChildren<Light>(true))
        {
            if (light.name.Contains("BASE"))
            {
                light.renderMode = LightRenderMode.ForcePixel;
                if (light.shadows == LightShadows.Soft) light.shadows = LightShadows.Hard;
            }
            else
            {
                light.renderMode = LightRenderMode.ForceVertex;
                light.shadows = LightShadows.None;
            }
        }
        Log("Lighting: studio lights in use (main light per-pixel with hard shadows, fill lights as vertex lights), flat ambient, fog off.");
    }

    // ===================================================================== geometry helpers

    private static void KeepFacing(Transform t, Vector3 oldCenter, Vector3 oldViewer, Vector3 newCenter, Vector3 newViewer)
    {
        Vector3 before = Flat(oldViewer - oldCenter);
        Vector3 after = Flat(newViewer - newCenter);
        if (before.sqrMagnitude < 0.0001f || after.sqrMagnitude < 0.0001f) return;
        float yaw = Vector3.SignedAngle(before, after, Vector3.up);
        t.RotateAround(oldCenter, Vector3.up, yaw);
    }

    private static float FitFootprint(Transform t, Vector2 maxFootprint)
    {
        if (!TryGetBounds(t.gameObject, out Bounds b)) return 1f;
        float k = Mathf.Min(1f, maxFootprint.x / Mathf.Max(0.01f, b.size.x), maxFootprint.y / Mathf.Max(0.01f, b.size.z));
        // footprint is axis-aligned; allow the object to fit either way round
        float kRot = Mathf.Min(1f, maxFootprint.y / Mathf.Max(0.01f, b.size.x), maxFootprint.x / Mathf.Max(0.01f, b.size.z));
        k = Mathf.Max(k, Mathf.Min(1f, kRot));
        if (k < 0.999f) t.localScale *= k;
        return k;
    }

    // Moves t so its bounds are centred on `spot` (XZ) with the bottom at `bottomY`.
    private static void MoveBoundsTo(Transform t, Vector2 spot, float bottomY)
    {
        if (!TryGetBounds(t.gameObject, out Bounds b)) return;
        t.position += new Vector3(spot.x - b.center.x, bottomY - b.min.y, spot.y - b.center.z);
    }

    private static Vector3 StandPoint(GameObject station, Vector3 viewer, float clearance)
    {
        TryGetBounds(station, out Bounds b);
        Vector3 dir = Flat(viewer - b.center);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.back;
        dir.Normalize();
        float half = Mathf.Max(b.extents.x, b.extents.z);
        Vector3 p = b.center + dir * (half + clearance);
        return new Vector3(p.x, viewer.y, p.z);
    }

    private static Vector3 BoundsCenter(GameObject go) => TryGetBounds(go, out Bounds b) ? b.center : go.transform.position;

    // How high the object's lowest point was above the old ground (terrain/rocks under it).
    private static float MeasureElevation(GameObject go, Bounds b, GameObject environment)
    {
        if (environment == null) return 0f;
        Physics.SyncTransforms();
        var origin = new Vector3(b.center.x, b.max.y + 5f, b.center.z);
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(environment.transform) && hit.point.y <= b.min.y + 0.05f)
                best = Mathf.Max(best, hit.point.y);
        }
        return float.IsNegativeInfinity(best) ? 0f : Mathf.Max(0f, b.min.y - best);
    }

    // Drumsticks are children of the drum kit / bongo but lie wherever they were dropped, so
    // they're left out of the size measurement (they still move together with the instrument).
    private static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (IsUnderStick(r.transform, go.transform)) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private static bool IsUnderStick(Transform t, Transform root)
    {
        for (Transform x = t; x != null && x != root; x = x.parent)
        {
            if (x.name.IndexOf("stick", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static bool TryGetFloorBounds(GameObject studio, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (GameObject root in StudioPrefabRoots(studio))
        {
            if (!PrefabName(root).Equals("Floor", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryGetBounds(root, out Bounds b)) continue;
            if (!any) { bounds = b; any = true; } else bounds.Encapsulate(b);
        }
        return any;
    }

    private static IEnumerable<GameObject> StudioPrefabRoots(GameObject studio)
    {
        return studio.GetComponentsInChildren<Transform>(true)
            .Select(t => t.gameObject)
            .Where(go => PrefabUtility.IsOutermostPrefabInstanceRoot(go))
            .ToList();
    }

    private static string PrefabName(GameObject instanceRoot)
    {
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
        return string.IsNullOrEmpty(path) ? instanceRoot.name : Path.GetFileNameWithoutExtension(path);
    }

    private static GameObject FindPrefabInstanceRoot(Scene scene, string prefabPath)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == prefabPath)
                    return t.gameObject;
            }
        }
        return null;
    }

    private static Component FindComponentByTypeName(Scene scene, string typeName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Component c = root.GetComponentsInChildren<Component>(true).FirstOrDefault(x => x != null && x.GetType().Name == typeName);
            if (c != null) return c;
        }
        return null;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    private static string Name(UnityEngine.Object o) => o != null ? o.name : "NOT FOUND";

    private static void Log(string line) => Report.AppendLine("• " + line);
}
