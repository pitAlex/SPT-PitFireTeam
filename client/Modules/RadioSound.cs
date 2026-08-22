using Comfort.Common;
using EFT;
using EFT.UI;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System;

namespace pitTeam.Modules
{
    internal class RadioSound : MonoBehaviour
    {


        private AudioClip radioClip;
        private AudioClip locationClip;
        private AudioClip radioBeepClip;

        public async void Enable()
        {

            List<AudioClip> sounds = await GetSound();
            radioClip = sounds[0];
            locationClip = sounds[1];
            radioBeepClip = sounds[2];
        }

        private async Task<List<AudioClip>> GetSound()
        {
            List<AudioClip> audioClips = new List<AudioClip>();
            audioClips.Add(await LoadAudioClip("file://" + ResolveSoundPath("radiochat.ogg")));
            audioClips.Add(await LoadAudioClip("file://" + ResolveSoundPath("locationping.ogg")));
            audioClips.Add(await LoadAudioClip("file://" + ResolveSoundPath("radiobeep.ogg")));

            return audioClips;
        }


        public static async Task<AudioClip> LoadAudioClip(string uri)
        {

            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
            {
                var asyncOperation = req.SendWebRequest();

                while (!asyncOperation.isDone) await Task.Yield();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var result = DownloadHandlerAudioClip.GetContent(req);

                    result.LoadAudioData();
                    while (result.loadState != AudioDataLoadState.Loaded) await Task.Yield();
                    return result;
                }
                else
                {
                    Logger.LogError($"Failed to load audio file: {req.error}");
                    return null;
                }
            }
        }

        public async void PlayRadioBeep()
        {
            try
            {
                float level = (pitFireTeam.pingRadioVolume.Value / 100f) * 0.3f;
                // Lazy load when async Enable() has not completed yet.
                if (radioBeepClip == null || radioBeepClip.length == 0)
                {
                    radioBeepClip = await LoadAudioClip("file://" + ResolveSoundPath("radiobeep.ogg"));
                }

                if (radioBeepClip == null) return;
                Singleton<GUISounds>.Instance.PlaySound(radioBeepClip, false, true, level);
            }
            catch (System.Exception e)
            {
                Logger.LogError(e);
            }
        }

        public async void PlayRadioSound(Vector3 worldPosition)
        {
            try
            {
                float level = (pitFireTeam.pingRadioVolume.Value / 100f) * 0.5f;
                if (radioClip == null || radioClip.length == 0)
                {
                    radioClip = await LoadAudioClip("file://" + ResolveSoundPath("radiochat.ogg"));
                }

                if (radioClip == null) return;

                try
                {
                    BetterAudio audio = Singleton<BetterAudio>.Instance;
                    if (audio == null)
                    {
                        Singleton<GUISounds>.Instance.PlaySound(radioClip, false, true, level);
                        return;
                    }

                    BetterSource source = audio.PlayAtPoint(
                        worldPosition,
                        radioClip,
                        0f,
                        BetterAudio.AudioSourceGroupType.Speech,
                        90,
                        level,
                        EOcclusionTest.None,
                        null,
                        false);

                    if (source == null)
                    {
                        Singleton<GUISounds>.Instance.PlaySound(radioClip, false, true, level);
                    }
                }
                catch
                {
                    Singleton<GUISounds>.Instance.PlaySound(radioClip, false, true, level);
                }
            }
            catch (System.Exception e)
            {
                Logger.LogError(e);
            }
        }

        public async void PlayLocationSound(Vector3 worldPosition)
        {
            try
            {
                float level = (pitFireTeam.statusSound.Value / 100f) * 0.6f;
                // Lazy load when async Enable() has not completed yet.
                if (locationClip == null || locationClip.length == 0)
                {
                    locationClip = await LoadAudioClip("file://" + ResolveSoundPath("locationping.ogg"));
                }

                if (locationClip == null) return;

                try
                {
                    BetterAudio audio = Singleton<BetterAudio>.Instance;
                    if (audio == null)
                    {
                        Singleton<GUISounds>.Instance.PlaySound(locationClip, false, true, level);
                        return;
                    }

                    BetterSource source = audio.PlayAtPoint(
                        worldPosition,
                        locationClip,
                        0f,
                        BetterAudio.AudioSourceGroupType.Speech,
                        90,
                        level,
                        EOcclusionTest.None,
                        null,
                        false);

                    // BetterAudio can fail silently (null source) if distance/rolloff checks fail.
                    if (source == null)
                    {
                        Singleton<GUISounds>.Instance.PlaySound(locationClip, false, true, level);
                    }
                }
                catch
                {
                    // Fall back to a guaranteed UI playback path if 3D source creation fails.
                    Singleton<GUISounds>.Instance.PlaySound(locationClip, false, true, level);
                }
            }
            catch (System.Exception e)
            {
                Logger.LogError(e);
            }
        }

        private static string ResolveSoundPath(string fileName)
        {
            string dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(dllDir))
            {
                throw new Exception("Cannot resolve plugin directory for sound loading.");
            }

            // Prefer root next to DLL, then resources subfolder.
            string direct = Path.Combine(dllDir, fileName);
            if (File.Exists(direct)) return direct;

            string resources = Path.Combine(dllDir, "resources", fileName);
            if (File.Exists(resources)) return resources;

            throw new FileNotFoundException($"Sound file not found: {fileName}. Checked: {direct} and {resources}");
        }
    }

}
