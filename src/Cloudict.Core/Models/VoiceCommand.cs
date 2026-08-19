using System;
using System.ComponentModel;

namespace Cloudict
{
    public enum CommandActionType
    {
        // دستورات تایپی
        TypeText,
        
        // دستورات کلیدی (پشتیبانی از تک کلید و ترکیب کلیدها)
        SendKeys,
        
        // دستورات تغییر زبان
        ChangeToFarsi,
        ChangeToEnglish
    }

    public class VoiceCommand : INotifyPropertyChanged
    {
        private int _id;
        private string _phrase;
        private CommandActionType _actionType;
        private string _actionValue;
        private bool _isEnabled = true;
        private DateTime _createdAt = DateTime.Now;
        private DateTime _updatedAt = DateTime.Now;
        private int _rowNumber;

        public event PropertyChangedEventHandler PropertyChanged;

        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged(nameof(Id));
                }
            }
        }

        public string Phrase
        {
            get => _phrase;
            set
            {
                if (_phrase != value)
                {
                    _phrase = value;
                    OnPropertyChanged(nameof(Phrase));
                    OnPropertyChanged(nameof(Command));
                }
            }
        }

        public CommandActionType ActionType
        {
            get => _actionType;
            set
            {
                if (_actionType != value)
                {
                    _actionType = value;
                    OnPropertyChanged(nameof(ActionType));
                    OnPropertyChanged(nameof(Action));
                }
            }
        }

        public string ActionValue
        {
            get => _actionValue;
            set
            {
                if (_actionValue != value)
                {
                    _actionValue = value;
                    OnPropertyChanged(nameof(ActionValue));
                    OnPropertyChanged(nameof(Parameter));
                }
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set
            {
                if (_createdAt != value)
                {
                    _createdAt = value;
                    OnPropertyChanged(nameof(CreatedAt));
                }
            }
        }

        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set
            {
                if (_updatedAt != value)
                {
                    _updatedAt = value;
                    OnPropertyChanged(nameof(UpdatedAt));
                }
            }
        }

        public int RowNumber
        {
            get => _rowNumber;
            set
            {
                if (_rowNumber != value)
                {
                    _rowNumber = value;
                    OnPropertyChanged(nameof(RowNumber));
                }
            }
        }
        
        // Properties for ListView display
        public string Command => Phrase;
        public string Action => GetActionTypeDisplayName();
        public string Parameter => ActionValue ?? "";
        
        private string GetActionTypeDisplayName()
        {
            return ActionType switch
            {
                CommandActionType.TypeText => "تایپ متن",
                CommandActionType.SendKeys => "ارسال کلید",
                CommandActionType.ChangeToFarsi => "تغییر به فارسی",
                CommandActionType.ChangeToEnglish => "تغییر به انگلیسی",
                _ => ActionType.ToString()
            };
        }
        
        public VoiceCommand() { }
        
        public VoiceCommand(string phrase, CommandActionType actionType, string actionValue)
        {
            Phrase = phrase;
            ActionType = actionType;
            ActionValue = actionValue;
            IsEnabled = true;
            CreatedAt = DateTime.Now;
            UpdatedAt = DateTime.Now;
        }
        
        public VoiceCommand(int id, string phrase, CommandActionType actionType, string actionValue)
        {
            Id = id;
            Phrase = phrase;
            ActionType = actionType;
            ActionValue = actionValue;
            IsEnabled = true;
            CreatedAt = DateTime.Now;
            UpdatedAt = DateTime.Now;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return $"{Phrase} -> {ActionType}: {ActionValue}";
        }
    }
}