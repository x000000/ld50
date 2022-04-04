using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = Unity.Mathematics.Random;

namespace com.x0
{
    public class MainMenu : MonoBehaviour
    {
        public TMP_InputField Input;
        public Button StartButton;
        
        private Random _rand = new((uint) DateTime.Now.Millisecond);
        private uint _seed;

        private void Start()
        {
            if (string.IsNullOrEmpty(Input.text?.Trim())) {
                GenerateSeed();
            }
        }

        public void GenerateSeed()
        {
            uint r = 0;
            while (r == 0) {
                r = _rand.NextUInt();
            }
            Input.text = r.ToString();
        }

        public void ValidateInput() => StartButton.interactable = ValidateInput(out _seed);

        private bool ValidateInput(out uint seed) => uint.TryParse(Input.text.Trim(), out seed) && seed > 0;

        public void StartLevel()
        {
            if (ValidateInput(out _seed)) {
                SceneManager.activeSceneChanged += OnSceneChange;
                SceneManager.LoadScene(1);
            }
        }

        private void OnSceneChange(Scene oldScene, Scene newScene)
        {
            SceneManager.activeSceneChanged -= OnSceneChange;
            
            foreach (var root in newScene.GetRootGameObjects()) {
                var generator = root.GetComponentInChildren<MazeGenerator>();
                if (generator != null) {
                    generator.Init(_seed);
                    return;
                }
            }
        }
    }
}
