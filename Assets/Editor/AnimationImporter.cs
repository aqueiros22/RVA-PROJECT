using System.IO;
using UnityEditor;

// Import rules for the Mixamo animations in Assets/Animations:
// humanoid rig, whole take, only the Idle loops, and the character stays on its card
public class AnimationImporter : AssetPostprocessor
{
    private bool IsAnimation =>
        assetPath.StartsWith("Assets/Animations/") && assetPath.ToLower().EndsWith(".fbx");

    void OnPreprocessModel()
    {
        if (!IsAnimation)
        {
            return;
        }
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
    }

    void OnPreprocessAnimation()
    {
        if (!IsAnimation)
        {
            return;
        }

        var importer = (ModelImporter)assetImporter;
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        bool loop = fileName.ToLower().Contains("idle");
        var takes = importer.defaultClipAnimations;
        // Keep existing clips (the animator controller references them by id); new ones are named after the file
        bool isNew = importer.clipAnimations.Length == 0;
        var clips = isNew ? takes : importer.clipAnimations;
        foreach (var clip in clips)
        {
            if (isNew)
            {
                clip.name = fileName;
            }
            foreach (var take in takes)
            {
                if (take.takeName == clip.takeName)
                {
                    clip.firstFrame = take.firstFrame;
                    clip.lastFrame = take.lastFrame;
                }
            }
            clip.loopTime = loop;
            clip.loopPose = loop;
            // Bake root motion into the pose: no turning or sliding off the card,
            // and the height follows the feet so Crouch lowers the body
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalPositionXZ = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = false;
            clip.heightFromFeet = true;
        }
        importer.clipAnimations = clips;
    }
}
