using System;
using System.ComponentModel;
using System.Security.Principal;
using AutoNotify;

namespace ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            HelloWorldGenerated.HelloWorld.SayHello();
            var person = new Person() {Name = "Bk"};
            Console.WriteLine(person.Name);

            var vm = new MyViewModel();
            vm.PropertyChanged += delegate(object? sender, PropertyChangedEventArgs eventArgs)
            {
                Console.WriteLine(eventArgs.PropertyName);
            };
            vm.Count = 10;
            vm.Text = "100";
        }
    }

    public partial class MyViewModel
    {
        [AutoNotify]
        private string _text = "private field text";

        [AutoNotify(PropertyName = "Count")]
        private int _amount = 5;
    }

    public partial class MainWindowViewModel
    {
        [AutoNotify]
        private string _firstName;

        [AutoNotify]
        private string _lastName;

        // [AutoNotify] 
        public string FullName => $"{FirstName} {LastName}"; // computed property!
    }
}