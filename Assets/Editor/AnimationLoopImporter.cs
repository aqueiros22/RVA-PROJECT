using UnityEditor;

// Idle animations in Assets/Animations always play their whole take and loop,
// even after the FBX is replaced by a longer one
public class AnimationLoopImporter : AssetPostprocessor
{
    void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith("Assets/Animations/") || !assetPath.ToLower().Contains("idle"))
        {
            return;
        }

        var importer = (ModelImporter)assetImporter;
        var takes = importer.defaultClipAnimations;
        // Keep the existing clips (the animator controller references them by id), only fix their range
        var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : takes;
        foreach (var clip in clips)
        {
            foreach (var take in takes)
            {
                if (take.takeName == clip.takeName)
                {
                    clip.firstFrame = take.firstFrame;
                    clip.lastFrame = take.lastFrame;
                }
            }
            clip.loopTime = true;
            clip.loopPose = true;
        }
        importer.clipAnimations = clips;
    }
}
