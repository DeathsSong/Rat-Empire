# Rat Habitat — Unity Vertical Slice

This is a separate Unity/C# project for the Rat Habitat game. The working browser prototype is intentionally not copied over or modified; keep it as the behavior reference and backup.

## Scope

The first vertical slice contains:

- One procedural 3D rat habitat
- Mabel and Otto as adult female/male founders
- Tap/click rat and habitat-object selection
- Portrait-first angled room presentation with rounded pastel low-poly scenery
- Expressive rounded rat faces, selection rings, tunnels, nest, and enrichment props
- Mouse-wheel/keyboard zoom on Windows and two-finger pinch zoom on touch screens
- Android touch input and Windows mouse/keyboard camera input
- Adult mate selection with Parent A / Parent B comparison
- B/b, C/c, D/d, and S/s coat genetics
- Albino masking, dilution, white spotting, and rare recorded mutations
- Size, health, and fertility inheritance
- Saved pregnancy timers, litter creation, parent IDs, generation, litter IDs, genotype, phenotype, traits, and timestamps
- Pinkie → Young Rat → Adult growth
- Pinkies rendered small and pink with hidden fur color
- Fur and markings revealed at the Young Rat stage
- Local JSON save/load in `Application.persistentDataPath`
- Developer testing controls for pregnancy completion and growth
- GUI build menu entries for Android APK and Windows test builds

Rabbits, economy, upgrades, Steam integration, multiplayer, and advanced art are deliberately outside this slice.

## Replaceable rat visual stages

`RatVisualFactory` and `RatVisualController` keep the visual prefab separate from `RatData`:

- `Rat_Pinkie_Prototype.prefab` is an asset-free procedural pinkie made from Unity primitives. It has pink skin, tiny ears, feet, eyes, and tail, and never receives fur or coat phenotype data.
- Young Rats and Adults use the same imported furry model. Young Rats use `GameConfig.YoungVisualScale` (currently `0.68`) and Adults use `GameConfig.AdultVisualScale` (currently `1.0`).
- Pinkie → Young Rat and Young Rat → Adult are smooth visual-only scale transitions. The growth system still owns the saved stage, age, timestamp, and developer growth anchor.
- When the young-rat stage is applied, the factory tints the imported renderers from the stored phenotype and adds a simple genetics spotting overlay when required. Pinkies are never tinted from genotype.

### Assigning the TurboSquid Hand Painted Rat model

The checked-in project does not include a purchased/downloaded FBX. After importing the legally obtained Hand Painted Rat FBX and its textures in the Unity Editor, either:

1. Place the model under `Assets/Resources/HandPaintedRat/HandPaintedRat.fbx`; the factory finds it automatically, or
2. Select `Game Bootstrap` in `Assets/Scenes/Main.unity` and drag the imported model/prefab into `Rat Visual Factory > Hand Painted Rat Prefab`.

The model's rig, Animator, textures, and animation clips remain on the instantiated adult/young visual. No pinkie download is needed. Until the adult asset is assigned, the existing genetics-driven procedural furry rat remains as a safe runtime fallback.

## Open on Windows

1. Install Unity Hub.
2. Install a Unity 2022.3 LTS editor (or a later Unity LTS editor) with **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK** selected.
3. In Unity Hub, choose **Add** → **Add project from disk** and select this folder.
4. Open `Assets/Scenes/Main.unity`.
5. Select the **Game** tab and choose **Free Aspect** or a portrait preset such as **540 × 960**.
6. Press the Play button. The habitat and two rats are created at runtime. A visible diagnostic badge should change from the camera test message to **3D scene ready — 2 rats loaded.**

The scene camera is explicitly enabled, tagged `MainCamera`, orthographic, aimed at the habitat, set to `0.1–100` clipping, and set to render every layer. A scene-owned **Bright Render Test** object creates a magenta unlit cube before gameplay startup, so the Game view has a guaranteed render proof. A startup guard also supplies a brightly colored diagnostic habitat if runtime construction stops early, so a blank Game view is not silent.

The project uses `Active Input Handling: Both` so the legacy mouse/touch polling and `StandaloneInputModule` remain compatible in Unity 2022.3 while the project can later adopt newer input actions. Scene startup does not depend on input: the camera, render test, habitat, rats, and UI initialize from `Awake` before the first input event.

If the diagnostic badge does not reach the ready message, open the Console immediately after pressing Play. The first startup log should be `[Rat Habitat] SceneVisibilityGuard.Awake started.` followed by `GameBootstrap.Awake started.` and `GameBootstrap.Awake completed.` The badge will show a visible fallback or error message instead of staying indefinitely in a boot state.

For a Windows build, use **Rat Habitat** → **Build Windows Test Player** in the Unity Editor menu.

For an Android APK, connect the Android module in Unity Hub, then use **File** → **Build Profiles** (or **Build Settings** in older Unity versions), select Android, switch platform, and use **Rat Habitat** → **Build Android APK**. The menu asks where to save the APK.

## Isolated interaction smoke test

Before diagnosing the rat scene, open `Assets/Scenes/InteractionSmokeTest.unity` and press Play. This scene intentionally contains no `GameBootstrap`, rats, habitat builder, breeding code, save code, or `InteractionManager`. It creates one large magenta cube with an enabled `BoxCollider`, one `MainCamera`, one `EventSystem`, and one `StandaloneInputModule` at runtime. Its direct mouse/touch test uses the unrestricted `Physics.Raycast` overload and does not use a layer mask.

The Game view diagnostic starts at **NO INPUT RECEIVED**. Click the large **UI BUTTON TEST** first; success changes the diagnostic to **UI BUTTON WORKS**. Then click the cube in the Game tab, or tap it on Android. A successful cube test changes the diagnostic to **INTERACTION SUCCESS**, turns the cube green, and shows the pointer screen coordinates. A miss reports **INPUT RECEIVED — NO COLLIDER HIT**; another collider reports **INPUT RECEIVED — HIT: [object name]**. Do not reconnect this test to rat selection until this isolated path passes.

## Binary handoff status

This source archive was prepared in a workspace without the Unity Editor, Android SDK, or Windows player toolchain, so an APK and Windows executable could not be generated here. The project includes the scene, runtime code, build settings, and GUI build menu so the binaries can be created from Unity Editor without terminal commands.

## Controls

- Android: tap a rat or habitat object; swipe the lower information panel to scroll; pinch over the habitat to zoom; tap buttons normally.
- Windows: left-click a rat or object; mouse wheel or `+`/`-` changes camera zoom; WASD or arrow keys pan the camera; Escape cancels breeding.

## Interaction setup

`Game Bootstrap` owns the single `InteractionManager` shown in the Main scene. It accepts a mouse-down or a completed, non-drag touch tap, creates a validated normalized ray with `Camera.main.ScreenPointToRay`, and uses the all-layer `Physics.RaycastAll(Ray, ...)` overload only for that input event. A child mesh hit resolves through an `InteractableObject` marker to its nearest `SelectableEntity` parent. Empty-floor taps clear the current selection.

The scene uses the manual interaction manager instead of a camera `PhysicsRaycaster`; the startup guard removes any `PhysicsRaycaster`, `Physics2DRaycaster`, or legacy camera `FlareLayer` that may have been added to the scene or a prefab. The FlareLayer audit is important because that legacy component can produce the `IsNormalized(dir, ...)` assertion before a user click. Runtime startup also keeps exactly one `EventSystem` and one compatible legacy `StandaloneInputModule`, matching **Active Input Handling: Both**. The Canvas keeps its `GraphicRaycaster` for buttons. Production UI-block checks query only `GraphicRaycaster` instances, so a stale world raycaster cannot be invoked while deciding whether a pointer is over a button or card. The page viewport and diagnostic text do not raycast, while visible cards, the eligible-mate viewport, modal blockers, and buttons remain intentional UI hit surfaces.

After a click or tap, the visible in-Game-view diagnostic overlay reports manager readiness, pointer coordinates, ray creation, raycast occurrence and hit count, resolved collider, interactable discovery, callback/panel state, and the A–E failure case. The same single diagnostic is written to the Console for debugging without any per-frame physics query.

## Design contract preserved from the browser prototype

- Only adult rats may breed.
- A pair must contain one female and one male.
- Pregnancy uses the short test timer from the prototype.
- Each allele is inherited from one parent; coat color is never assigned independently of genotype.
- Pinkies keep their genotype from birth, but show `Fur color: Unknown` until they become Young Rats.
- Normal growth is timestamp-based. Developer growth moves the saved growth anchor without changing the original birth timestamp.
- Save/load uses local JSON and preserves pending pregnancies, litters, parents, pups, genes, traits, and timestamps.
