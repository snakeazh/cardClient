using System;
using Framework.UI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Framework.UI.Binding
{
    public sealed class BindingContext : IDisposable
    {
        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private bool _disposed;

        public void Add(IDisposable disposable) => _disposables.Add(disposable);

        public void BindText(TMP_Text text, ObservableProperty<string> source)
        {
            EnsureAlive();
            if (text == null || source == null)
            {
                return;
            }

            Add(source.Subscribe(value => text.text = value ?? string.Empty));
        }

        public void BindText(Text text, ObservableProperty<string> source)
        {
            EnsureAlive();
            if (text == null || source == null)
            {
                return;
            }

            Add(source.Subscribe(value => text.text = value ?? string.Empty));
        }

        public void BindText<T>(TMP_Text text, ObservableProperty<T> source, Func<T, string> formatter)
        {
            EnsureAlive();
            if (text == null || source == null || formatter == null)
            {
                return;
            }

            Add(source.Subscribe(value => text.text = formatter(value) ?? string.Empty));
        }

        public void BindCommand(Button button, IRelayCommand command)
        {
            EnsureAlive();
            if (button == null || command == null)
            {
                return;
            }

            void OnClick() => command.Execute();
            void OnCanExecuteChanged() => button.interactable = command.CanExecute();

            button.onClick.AddListener(OnClick);
            command.CanExecuteChanged += OnCanExecuteChanged;
            button.interactable = command.CanExecute();

            Add(new ActionDisposable(() =>
            {
                button.onClick.RemoveListener(OnClick);
                command.CanExecuteChanged -= OnCanExecuteChanged;
            }));
        }

        public void BindToggle(Toggle toggle, ObservableProperty<bool> source, bool twoWay = true)
        {
            EnsureAlive();
            if (toggle == null || source == null)
            {
                return;
            }

            var syncing = false;

            Add(source.Subscribe(value =>
            {
                if (syncing)
                {
                    return;
                }

                syncing = true;
                toggle.isOn = value;
                syncing = false;
            }));

            if (!twoWay)
            {
                return;
            }

            void OnValueChanged(bool value)
            {
                if (syncing)
                {
                    return;
                }

                syncing = true;
                source.Value = value;
                syncing = false;
            }

            toggle.onValueChanged.AddListener(OnValueChanged);
            Add(new ActionDisposable(() => toggle.onValueChanged.RemoveListener(OnValueChanged)));
        }

        public void BindSlider(Slider slider, ObservableProperty<float> source, bool twoWay = true)
        {
            EnsureAlive();
            if (slider == null || source == null)
            {
                return;
            }

            var syncing = false;

            Add(source.Subscribe(value =>
            {
                if (syncing)
                {
                    return;
                }

                syncing = true;
                slider.value = value;
                syncing = false;
            }));

            if (!twoWay)
            {
                return;
            }

            void OnValueChanged(float value)
            {
                if (syncing)
                {
                    return;
                }

                syncing = true;
                source.Value = value;
                syncing = false;
            }

            slider.onValueChanged.AddListener(OnValueChanged);
            Add(new ActionDisposable(() => slider.onValueChanged.RemoveListener(OnValueChanged)));
        }

        public void BindInputField(TMP_InputField input, ObservableProperty<string> source, bool twoWay = true)
        {
            EnsureAlive();
            if (input == null || source == null)
            {
                return;
            }

            var syncing = false;

            Add(source.Subscribe(value =>
            {
                if (syncing)
                {
                    return;
                }

                syncing = true;
                input.text = value ?? string.Empty;
                syncing = false;
            }));

            if (!twoWay)
            {
                return;
            }

            void OnValueChanged(string value)
            {
                if (syncing)
                {
                    return;
                }

                syncing = true;
                source.Value = value;
                syncing = false;
            }

            input.onValueChanged.AddListener(OnValueChanged);
            Add(new ActionDisposable(() => input.onValueChanged.RemoveListener(OnValueChanged)));
        }

        public void BindImageSprite(Image image, ObservableProperty<Sprite> source)
        {
            EnsureAlive();
            if (image == null || source == null)
            {
                return;
            }

            Add(source.Subscribe(sprite => image.sprite = sprite));
        }

        public void BindImageColor(Image image, ObservableProperty<Color> source)
        {
            EnsureAlive();
            if (image == null || source == null)
            {
                return;
            }

            Add(source.Subscribe(color => image.color = color));
        }

        public void BindInteractable(Selectable selectable, ObservableProperty<bool> source)
        {
            EnsureAlive();
            if (selectable == null || source == null)
            {
                return;
            }

            Add(source.Subscribe(value => selectable.interactable = value));
        }

        public void BindCanvasGroup(CanvasGroup group, ObservableProperty<float> alpha = null, ObservableProperty<bool> interactable = null)
        {
            EnsureAlive();
            if (group == null)
            {
                return;
            }

            if (alpha != null)
            {
                Add(alpha.Subscribe(value => group.alpha = value));
            }

            if (interactable != null)
            {
                Add(interactable.Subscribe(value =>
                {
                    group.interactable = value;
                    group.blocksRaycasts = value;
                }));
            }
        }

        public void BindActive(GameObject target, ObservableProperty<bool> source)
        {
            EnsureAlive();
            if (target == null || source == null)
            {
                return;
            }

            Add(source.Subscribe(value =>
            {
                if (target.activeSelf != value)
                {
                    target.SetActive(value);
                }
            }));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _disposables.Dispose();
        }

        private void EnsureAlive()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BindingContext));
            }
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action _dispose;

            public ActionDisposable(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }
    }
}
