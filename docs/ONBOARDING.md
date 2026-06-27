# WOMENACE Onboarding

Hello girlies, this document goes over everything you need to start work on WOMENACE. I also hope this document can help with anyone looking to see what a Jiangyu project is *supposed* to look like.

Before we begin I'm assuming you:

1. Know what WOMENACE is, and why you're here
2. Know (or have a rough idea on) what [Jiangyu](https://github.com/antistrategie/jiangyu) is
3. Have set it up (either via Studio or CLI)
4. Am able to compile WOMENACE on your computer and run it in-game

For work on WOMENACE specifically, I also want you to have:

1. A text editor. If you want to write C# code this obviously needs to be something that is able to do that. [Visual Studio Code](https://code.visualstudio.com/) is always a safe bet
2. If you're going to write C#, you'll want a way to read the game's types. I use a decompiler -- [ILSpy](https://github.com/icsharpcode/ILSpy), or [ilspy-vscode](https://github.com/icsharpcode/ilspy-vscode) if you want it in your editor -- pointed at the interop assemblies in `MelonLoader/Il2CppAssemblies/` (that's where the `Il2CppMenace.*` types live, and it's exactly what your code references)
3. Optionally, [UnityExplorer](https://github.com/yukieiji/UnityExplorer) for poking the *live* game at runtime.
4. You also need to know how to use Git and work your way around GitHub (Git and GitHub are different things). I won't be going through that, but there are lots of resources on the internet.

Most of this doc goes over general ideas rather than walking you through every little corner of the mod, because the mod itself is honestly fairly simple (at least by my standards). You **should** be able to work most of it out just by browsing the project and looking at shit. The actually hard part is the thing underneath all of it -- a real feel for how MENACE is put together internally. That's slow to build and there's no shortcut, so what I want to be doing is pointing you at the ideas and the tools to build it faster, rather than handing you a list of facts that'll be stale by the next game update.

The thing about modding is that it's just a lighter version of game dev. There are also circumstances (like what we have currently with MENACE) where there are no officially supported tools to create mods, requiring us to reverse engineer our own.

So even if game dev is a mixed discipline, where a pure artist, or a pure programmer could create or at least start making a game on their own, modding in our case is a little different -- the reverse engineering requirement makes it a non-starter for all non-programmers.

This is what Jiangyu is for. I've done all the reverse engineering for you.

## The section where I get way too personal talking about Jiangyu while failing to give you the information you actually need

> Hello, document reviewer **beanpuppy** chiming in here. After reading through this section again, I feel my writer brain got the better of me, and it's way too long and there are too many things hidden in sub-text. But uhh... I'm not going to change any of that because this is MY safe space, and it's more fun for me this way (look dude, I just really like writing).
>
> Good luck! Hope you've worked on your media literacy recently!

Ok, let's start with something spicy -- if I were to start all over again, I would **not** make Jiangyu. All capabilities that Jiangyu is able to do would just be in here and specific to WOMENACE only. I would not split my efforts into two projects again.

But to explain that, I think I need to go through why Jiangyu exists right now. And to be honest, there's really only one reason.

The previous modkit literally *rage-baited* me into it.

I don't like talking shit about other people's work, I think it's poor form and just makes me feel a little bad in general. So all I'm going to say is that the MMK is some of the worst engineering I have ever seen in my life. 

One thing I've learnt throughout my years doing this is that all software projects are a little dogshit, all software projects are piles and piles of compromises. I expect things to give me the ick when going into any codebase for the first time, that's normal.

Programming is fundamentally about building a theory, a shared mental model of how a system works, why it works that way, and how it should evolve. The source code is merely a written representation of this theory, and like all representations, it's lossy.

When you don't have that theory, any new codebase is going to look alien and strange. Reading the code is how the previous developers communicate with you their mental model of the system. And like with all communication not everyone interprets the same signs and symbols the same way -- that's fine, that's just part of being human.

It is blindingly obvious to me that the MMK -- and I'm sorry, I genuinely can't think of a kinder way to put this -- was *not* created by people. There's no theory behind it. No mental model, no coherent vision, nothing for the code to be a lossy representation of.

I'm struggling to find a metaphor that I can use to help non-programmers understand this (probably because programming is all I know). It's not like the codebase is inscrutable... it just doesn't work "in theory". I get that's strange to say since it's working "in practice" (well it actually isn't, but you get what I mean), but it's like getting a letter from someone who doesn't speak the language and wrote it with a phrasebook.

Every sentence is real, every word spelled right, and "in practice" you can sort of follow it. But no one's actually talking to you. No thought behind the words, no idea being communicated, just phrases that happened to line up. "In theory" it's a letter, but there's just nobody on the other end.

Yeah, I guess that metaphor works in more ways than one actually.

...

Ok that was... a lot more than the one thing I wanted to limit myself to, let's get back on topic.

Making Jiangyu made me realise why no *person* made the MMK. It's a lot of fucking work, and I can see why someone took the easy way with it. I've tried doing what I can, but there are certain expectations (at least to me) that calling it a "general purpose modding platform" comes with.

But it's fine, the hard parts are done now, and people are using it (some people even like it!). I wouldn't say I regret making it or anything. At the very least I learnt a lot, and that probably matters more than anything (to me).

I suppose since I've been harping on it so much, it's only fair I talk about the "theory" of Jiangyu.

Jiangyu is meant to do two things:

1. Support work on a Girls' Frontline mod for MENACE
2. Support work on other mods for MENACE

Specifically in that order.

What this means in practice is that WOMENACE gets priority on any features. If WOMENACE needs something from Jiangyu, **it gets it**. If for whatever reason another mod wants something that conflicts with WOMENCE, **it's not going to happen**. I can make compromises (and I already have), but the needs of the few will always outweigh the needs of the many.

Jiangyu is also highly structured around my (and by extension WOMENACE's) workflow, which is by a majority largely text and CLI based (and why I expect you to have a text editor).

Yes this does in fact mean Jiangyu Studio is a second class citizen and is the prime example for one of the compromises I've made for the needs of the many.

But the text-and-CLI thing runs deeper than me just being a terminal gremlin. The real theory is that WOMENACE is mostly *data*, and the reason it can be is that MENACE itself is built mostly out of data too.

That's pretty normal for Unity games, and for something like MENACE it's basically mandatory. It's a turn-based tactics game, and that genre lives and dies on content: hundreds of units and weapons and abilities that are mostly just different numbers to balance. You can't sanely build that by hardcoding every unit, so the devs did the obvious thing and made each unit a *template* -- a bag of fields pointing at other templates for its animator, its weapons, its visual. The more a game pushes into data like this, the more of it you can reach without ever touching its compiled code, and MENACE pushes a lot. That's the single biggest reason any of this is moddable at all.

So modding with Jiangyu is mostly authoring data: adding a character, tweaking a weapon, etc, almost none of it touches C#. KDL is just the nicest way I've found to write that data. You don't need to write code to contribute, which is the whole difference between "only programmers can help" and "anyone willing to learn a bit of syntax can".

I'll be honest about the rest, though. KDL is nice *to me*. I've shaped it into a little [domain-specific language](https://en.wikipedia.org/wiki/Domain-specific_language) that works the way my programmer brain does, where you run operations, imperatively, to build a template up. The upside, for me at least, is that there's almost no abstraction between what I write and what `Jiangyu.Loader.dll` does.

When I read a KDL file I can see the operations it'll actually run, the same calls I'd be making by hand if I were driving the game's templates from code. Nothing sits in between turning my intent into something else. 

The downside is that plenty of non-programmers will take one look and decide Jiangyu is too complicated. That's just how it is, and to be honest I don't care for changing it. If someone working on WOMENACE finds it complicated I will literally shove them into a voice or text channel and hold their hands walking them through everything. It isn't a problem for *us*, so it doesn't change.

All of this lives in files. A Jiangyu mod is *entirely* described by its source tree -- the KDL, the assets, the manifest, the lot -- sitting in a folder you can throw in git. If it's not in the repo, it doesn't exist. This is all your standard programming stuff.

I'll say again that game dev is a mixed discipline. There are countless examples of non-programmers making amazing, successful games. But by virtue of video games implicitly relying on a computer, they always need to start with code, and the tools one would need to not require code requires a programmer to first build it.

So if we're just basing it off that -- my job as a programmer to build tools that empower users with computers, have I done a good job? Prolly not to be honest. I mean, the two priorities I stated earlier literally go against that. It's a software project full of compromises and is to be frank, a little dogshit. Though I should specify it's specifically *my* dogshit, mind you.

But none of that matters to me as long as I get my Girls' Frontline mod.

## How to actually work on WOMENACE

### KDL/Templates

Almost everything you do on WOMENACE is KDL- well actually, most of the lines of code are probably in C# at this point, but that's the nature of code being less abstracted (requires more to do the same amount). Either way, editing templates is the easiest way to get started on a high impact area.

First of all, **write templates in a real text editor.** That is the intended experience, full stop. A large portion of the mod is a folder of `.kdl` files, and a proper editor (VS Code, or whatever you already like) gives you the things that make that pleasant: find-and-replace across the whole project, git diffs in the gutter, etc. This is the workflow Jiangyu is built around.

I'm not going to re-document the syntax here. The Jiangyu docs cover [patches, clones, operations and field paths](https://antistrategie.github.io/jiangyu/templates) properly -- that page is the real reference. 

Studio also has a visual editor for KDL files, it shows a template as a set of expandable cards instead of raw text, and it reads and writes the same KDL underneath, so it's never doing anything off to the side. 

This was an attempt to make KDL authorship look more friendly, but I still wouldn't recommend it for anything other than a learning tool. When you don't yet know what fields a template has or what an operation looks like, poking around in it and watching the KDL it produces can be a good way to build the mental model. But treat it as training wheels. 

Once you know the shape of things it starts to feel slow, and the part of KDL I actually like (seeing the operations directly) gets buried under buttons. You can learn with it, but at some point you need to move to a text editor.

To find *what* to change in the first place there's the template browser, the other big part of Studio. The game has a huge number of templates, so the browser lets you search them and read their real in-game values.

I suggest starting with looking at templates that have been cloned in WOMENACE already and searching them up in the browser, seeing how it looks there and how we've edited the clones.

Eventually, through enough experience you'll build up a mental model of how data is structured in MENACE, and it'll become a lot easier and faster to edit whatever you want.

### Assets

This is the part of the mod is mostly visual, so there's not much I can go through with you here, but let's go over some general concepts.

**We add, we don't replace.** Everything WOMENACE ships is a *new* thing sitting alongside vanilla MENACE; Never is a vanilla asset overwritten. So all of it lands under `assets/additions/`. Even if we want to "replace" something, we ship it as an addition and point the thing we want to replace to our new addition instead of "replacing" the old thing.

Anything in `assets/additions/` should be self-explanatory, I'm sure you know how audio and image files work and how to edit them.

The real meat is in `unity/` which is a full Unity project, though not setup as much as you would when actually making the game. We need this for the visual half of a MENACE unit (its mesh, skeleton, materials, animations) that ships as a Unity *prefab* packed into an AssetBundle, and the only thing that can build one of those is Unity itself. So `unity/` is where the 3D work happens, and a couple of Jiangyu bake utilities turn your raw models into game-ready prefabs.

The project has three parts worth knowing. `Assets/Imported/` holds reference copies of vanilla things we bake against (the stock female soldier rig, a few vanilla weapons). `Assets/Authored/` is your raw input per character or weapon (the model files you bring in). `Assets/Prefabs/` is the baked output, the actual prefabs the mod ships. A template then points its `Prefabs`/`Model` field at one of those with `asset="..."`, and that's the whole bridge between the data side and the visual side.

**Characters (PMX to MENACE).** This one is almost too good. MENACE's soldiers all use Unity's standard *humanoid* rig, and Unity can automatically retarget animation between any two humanoid rigs. So if we put our Doll on that same humanoid skeleton, she inherits every soldier animation MENACE already has, walking, aiming, shooting, dying, all of it, for free. We don't author a single animation.

The catch is getting her *onto* that rig. Normally this is a manual job: you import the target armature into Blender, re-rig your model onto it, and re-paint all the vertex weights, so the mesh deforms properly. It's slow, finicky work. There's a couple "auto painters" out there, but none of them work particularly well with how complex GFL2 character models are.

So instead there's a script (`pmx_to_menace.py`) that does most of it, driven by a small JSON config you write per character. Most of the config is just paths, but the real work is the `bone_map`: pairing each PMX bone with the MENACE humanoid bone it should become. Get that right and the script handles the rest, renaming the skeleton, calibrating it to a reference soldier's T-pose, remapping the weights, and baking the LODs.

It only deals in absolutes, though, so it can't read your intent. You'll sometimes still go into Blender afterwards and fix weights by hand, the classic case being a skirt or dress that needs to follow the legs instead of hanging stiff.

After Blender, the glTF goes through `BakeHumanoid` to become the actual addition prefab. `BakeHumanoid` is a Unity Editor tool that ships with Jiangyu, synced into your project under `unity/Assets/Jiangyu/Editor/`, so it's already there and you don't write it. Open the Unity project, find **Jiangyu → Bake humanoid prefab from glTF...** in the menu bar, point it at your authored glTF folder and a reference soldier prefab, give it a name, and hit bake. It builds the humanoid Avatar from your skeleton, copies the ragdoll bits off the reference soldier, and writes the finished prefab into `Assets/Prefabs/`. (There's also a command-line entry point if you ever want to bake without opening the editor.)

**Voice.** A Doll talks, and that's its own little pipeline. The lines are ripped from GFL2. They come in hot (GFL2's audio is much louder than MENACE's), so first you normalise them down to vanilla loudness, then run them through transcription and translation to get the JP and EN subtitle text. The files themselves are nothing special. The work is wiring them up: you clone a SoundBank so the game knows about your clips, and clone the relevant ConversationTemplates so the right line fires at the right moment, her arrival, a kill, going down, and so on. That wiring is all KDL, so it leans on everything from the templates section.

**Weapons.** A weapon is also just a prefab, and it follows the same shape: a GFL2-ripped OBJ goes through `bake_weapon.py` in Blender, then `BakeWeapon` (the same kind of Jiangyu editor tool as above) turns it into the prefab. There's some by-hand fiddling in Blender to line the gun up so it sits in the soldier's hands properly, and then you clone a WeaponTemplate to point at it.

If you want the gun to *sound* like itself too, that's an optional extra. Rip the gunshot SFX from GFL2, run it through a bake script that derives the close and distant variants the game expects, then clone a SoundBank and route it into the weapon's fire skills. Same SoundBank idea as the voice lines, just pointed at gunfire instead.

**New/custom animations (specifically Sinbreaker).** Everything above leans on humanoid retargeting. Sinbreaker (Voymastina's mech) isn't a humanoid, nor can it be correctly represented by the skeleton of any other entities in the vanilla game. In order to correctly animate Sinbreaker we **have** to bring our own ones in (or in our case, take them from GFL2).

This applies to anything we bring in that can't reuse an existing rig, Sinbreaker is just the only example we've got so far. The reason comes down to how Unity handles rigs. A humanoid rig has a standard skeleton, so Unity can replay any animation on any other humanoid, that's the retargeting the Doll pipeline leans on. Anything that doesn't fit that mould (a mech, a creature, whatever you dream up) ends up on a generic rig, which has none of it. Its animation curves are bound to specific named bones, so a clip and the skeleton it was built for are effectively one object. You can't lift a clip off one rig onto another, and you can't borrow MENACE's animations because they were authored against MENACE's own rigs.

So you bring the whole package in together: the model, its skeleton, and its clips. For Sinbreaker that meant pulling all three out of GFL2. In Unity you assemble them into a self-contained prefab with its own AnimatorController driving its own clips, and bake it to a bundle like anything else.

The last piece is getting MENACE to actually drive it. The game has no idea what states your controller has, it just emits gameplay signals, this unit is moving, it used a skill, it died. So you write a bit of C# that listens for those and pokes the matching parameters on your controller. For Sinbreaker that's most of what `VoymastinaMechSystem` does (set a trigger when she fires the rocket, set a float while she's moving), plus the extra hacks a flying robot needs that a soldier doesn't, like scaling her speed so the hover reads as flight and stopping her vaulting cover she has no animation for.

### C#

Now we're finally at the good part :) As you may have gathered from like the first third of this document, I'm pretty- let's just say... passionate about code :)

Code is the difference between a standard "add new squad leaders" mod and an overhaul adding new systems and behaviour. It's certainly the area of the mod that allows us to exercise the most creative freedom and through Il2CppInterop, there isn't really anything we can't change about the game.

But we should get you grounded on a few terms first if you've never modded a Unity game first.

Most Unity games are built using **Mono** -- the game's code ships as managed `.dll` files, the exact format your own C# compiles to, so you can open them in a decompiler, read them like source, reference them directly, and patch them at runtime.

MENACE doesn't ship like that, because it's built with **IL2CPP** (Intermediate Language To C++). Unity takes the compiled C# and runs it through a translator that emits C++, then compiles that down to native machine code. By the time it reaches you the game has stopped being C# at all. It's a native binary, the same as something written in C or Rust or would you believe it - C++, and there's no managed DLL to reference, no method to politely override, just machine code and a big blob of metadata describing the types that used to exist.

This is usually done for performance reasons and for the most part isn't to spite modders and make things harder for us (usually), it's just an unfortunate side effect.

This is where MelonLoader comes in.

MelonLoader is a mod loader for Unity games, and the part we care about is what it does on the way in. It reads that big blob of metadata and, through a tool called Il2CppInterop, *generates* a set of managed proxy assemblies: real `.dll` files that mirror every type the native game has. Each proxy is a thin shell, same name, same fields, same method signatures as the original, but the method bodies are just stubs that marshal the call across into the native code. You write normal C# against these proxies, and at runtime your calls land on the real game.

So we end up roughly back where we started. We still can't read the game's logic (the actual method bodies are off in native land), but we *can* reference its types and call its methods like any other library. Those generated assemblies live in `<game>/MelonLoader/Il2CppAssemblies/`, and the `code/` project already references them.

Which finally brings us to **Il2CppMenace**. That's just what MENACE's own code looks like once it's been through that generator. The game's types originally lived under a `Menace` namespace (`Menace.Tactical.Actor` and friends), and the generator slaps an `Il2Cpp` prefix on the front of everything, so the same type is `Il2CppMenace.Tactical.Actor` in your code. Anything from Unity itself keeps its normal `UnityEngine.*` name. So when you see `Il2CppMenace.*` littered through WOMENACE's code, that's the game's own guts, reached across the bridge MelonLoader built for us.

So your code can name every type the game has, but not see what any of its methods actually do. Sooner or later you'll want to read the game properly, and there are two ways in.

The first is a decompiler, for the static types. The proxy assemblies are ordinary `.dll` files, so anything that reads .NET reads them. You can use [ILSpy](https://github.com/icsharpcode/ILSpy), or [ilspy-vscode](https://github.com/icsharpcode/ilspy-vscode) if you'd rather keep it in your editor. Point it at `MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll`, search for a type, and the game's whole type surface is laid out for you. You won't get the method bodies (the native stubs again), so you can see exactly what a thing *is* without seeing what it *does*. That's usually enough, as long as the devs have named their shit correctly, you can figure it out. And most of the time you just need to find the right type or method to work with, not reverse-engineer the logic behind it.

The second is [UnityExplorer](https://github.com/yukieiji/UnityExplorer), for the *live* game. It injects into the running game and lets you walk the actual scene: every GameObject, the components hanging off it, their values right now, the whole UI tree. It's the quickest way to answer "what is this screen actually built from" or "what's on this actor".

Right, so you can read the game. Now, how do you change it?

Mostly you won't be wrestling raw `Il2CppMenace` types by hand. That surface is enormous and grim to touch directly, all native interop and quirks, so Jiangyu wraps the bits you'll actually reach for, looking up templates, spawning units, building tooltips, playing audio, into a normal-feeling SDK (the `Jiangyu.Game` namespace). You only drop down to the raw types when you want something it hasn't wrapped yet. The [SDK docs](https://antistrategie.github.io/jiangyu/sdk/) are the real reference, here I'll just sketch the shape.

Your code tends to take one of two shapes. The common one is a **custom type**: when the thing you're adding is really a new kind of effect, condition, or value a template should be able to use, you write a class, tag it `[JiangyuType("Name")]`, and the game constructs it straight from a KDL `type=` slot exactly like one of its own. This is just a class the data can reach for. There's a [guide for it](https://antistrategie.github.io/jiangyu/sdk/template-types).

The other is a **system**: when you're adding behaviour that reacts to the game as it runs, you subclass `JiangyuSystem`, one per feature, and override the lifecycle hooks you care about. The loader spins them up and drives them for you.

A system reacts to the game through the SDK's **hooks**: an event bus for the moments the game broadcasts, a kill, a leader hired, an operation finishing. You subscribe to the ones you want and respond. When no hook covers the exact moment you need, you reach for the escape hatch, a **Harmony patch**, naming a game method by string and running your own code before or after it. That's how the mech stops itself vaulting cover it has no animation for, by patching the movement method directly. There's a [full list of hooks](https://antistrategie.github.io/jiangyu/reference/hooks) for what's already on the bus.

If you know how to code, I'm also assuming you know how to learn everything else you need. The best way to figure stuff out is to just go look at the code we have already, but there's one more thing worth noting -- raw IL2CPP keeps a few sharp edges the SDK can't fully hide, the classic being that you have to *index* one of the game's lists rather than `foreach` it, because the enumerator quietly refuses to move. So instead of the obvious:

```csharp
// wrong: the Il2Cpp enumerator never advances, so this misbehaves
foreach (var t in DataTemplateLoader.GetAll<EntityTemplate>())
    Use(t);
```

you cast the collection to a list and walk it by index:

```csharp
// right: index it
var all = DataTemplateLoader.GetAll<EntityTemplate>();
var list = all.TryCast<Il2CppSystem.Collections.Generic.IReadOnlyList<EntityTemplate>>();
for (var i = 0; i < list.Count; i++)
    Use(list[i]);
```

We keep this exact wrapper in `Templates.cs`, so nobody has to relearn it every time.
