using System;
using System.Data.Common;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using System.Threading;

namespace Disposable
{
    /*
     SELECT count(*) FROM sysprocesses where program_name = 'disposeTest' 
     */
    /*
     1 - Dispose of IDisposable objects as soon as you can (wykorzystuj using tam gdzie się da)
     2 - If you use IDisposable objects as instance fields, implement IDisposable (jeżeli używamy właściwości implementujących IDisposable, to klasa też powinna implementować IDisposable)
     3 - Allow Dispose() to be called multiple times and don't throw exceptions (przy implementacji IDisposable najlepiej korzystać z szablonu podpowiadanego przez Visual Studio)
     4 - if you implement IDisposable in a class which can be inherited, do your disposal logic in a protected virtual method ()
     5 - If you use unmanaged resources, declare a finalizer which cleans them up (implementacja finalizera pozwala na sprzątnięcie Unmanaged Resources przez GC jeżeli użytkownik o tym zapomni)
     6 - Enable static analysis with rule CA2000 (statyczna analiza kodu sprawdza gdzie using powinien zostać wykorzystany a nie został)
     7 - Know your domain (nie wszystko pokaże CA2000, klasy takie jak Stream, SqlConnection, IServiceScope powinny być czyszczone jak najszybciej, ale Task, DbContext i HttpClient najczęściej są zarządzane inaczej)
     8 - Implement IAsyncDisposable if your class uses an async disposable field
     */


    /*
     - Jeżeli wykorzystujemy obiekty implementujące IDisposable to w naszej klasie też powinniśmy implementować IDisposable
     - Zasoby niezarządzane w .NET to między innymi połączenia do baz danych, połączenia do plików, połączenia http
     - Metoda Dispose() jest automatycznie wywoływana na końcu bloku using(resource){}
     - Dispose() jest wywoływane nawet jeżeli wewnątrz bloku using znajduje się "return" 
     - Wielokrotne wywołanie metody Dispose() na obiekcie nie może powodować błędów!
     - Finalizer to sposób na posprzątanie zasobów jeżeli użytkownik nie wywoła Dispose() (jest wywoływany przez GC)
     - Finalizer posiada składnię ~Object ();
     - Finalizer nie może sprzątać Managed Resources ponieważ jest wywoływany przez GC i możemy nie chcący skasować potrzebne obiekty
     - Finalizer powoduje że obiekt jest dłużej przechowywany w pamięci (jest inaczej traktowany przez GC)

    Istotne informacje dla nowoczesnych aplikacji z kontenerem DI
    https://app.pluralsight.com/course-player?clipId=e15243f9-b899-4c65-a592-70fa336ab91b
     */
    class Program
    {
        static void Main(string[] args)
        {
            string connectionString = "server=.;database=SWS_TEST_NEW;trusted_connection = true;Connection Timeout=3;App = disposeTest";
            string nl = Environment.NewLine;
            Console.WriteLine($"1 - bez dispose {nl}2 - z dispose {nl}3 - wielokrotny dispose {nl}4 - wskaźnik bez dispose {nl}5 - wskaźnik z dispose{nl}6 - wskaźnik bez dispose z finalizerem{nl}");
            var key = Console.ReadKey().KeyChar;
            Console.WriteLine($"{Environment.NewLine} uruchamiam {key}...");
            switch (key)
            {
                case '1': bezDispose(); //kolejne połączenia do bazy danych zapychają pulę 100 domyślnych dostępnych połączeń i sypie wyjątkami
                    break;
                case '2': zDispose(); //połączenia są zamykane po wykorzystaniu wszystko ok
                    break;
                case '3': wielokrotnyDispose(); //przykład prawidłowej i nieprawidłowej implementacji IDisposable, nieprawidłowa wyrzuca wyjątek po 2 krotnym uruchomieniu metody Dispose()
                    break;
                case '4': wskaznikBezDispose(); //rezerwuje 1GB pamięci i nigdy jej nie zwalnia
                    break;
                case '5': wskaznikZDispose(); //kolejne wywołania rezerwują 100 MB i od razu sprzątają
                    break;
                case '6': wskaznikBezDisposeZFinalizerem(); //rezerwuje 1GB ale na koniec zasoby są zwalniane przez GC dzięki wykorzystaniu finalizera
                    break;
                default:
                    break;
            }
            Console.WriteLine("Koniec...");
            Console.ReadLine();

        //-----------------
        // metody lokalne
        //-----------------
        void bezDispose()
        {
            //Przepełnienie puli otwartych połączeń powoduje wyrzucanie wyjątków
            for (int i = 0; i < 1000; i++)
            {
                try
                {
                    NotDisposed notDisposed = new NotDisposed(connectionString);
                    notDisposed.GetDate();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message.Split(Environment.NewLine)[0]);
                }
            }
        };

        void zDispose()
        {
            //Wersja wykorzystująca DISPOSE
            for (int i = 0; i < 1000; i++)
            {
                using (var com = new Disposed(connectionString))
                {
                    com.GetDate();
                }
            }
        }

        void wielokrotnyDispose()
        {
                //Dispose() wywołane wielokrotnie nie może wyrzucać wyjątku!
                using (var com = new DisposedCorrectly(connectionString))
                {
                    com.GetDate();
                    com.Dispose();
                }

                using (var com = new Disposed(connectionString))
                {
                    com.GetDate();
                    com.Dispose();
                }
            }

        void wskaznikBezDispose()
        {           
                for (int i = 0; i < 10; i++)
                {
                    WithPointer withPointer = new WithPointer();
                    withPointer.AllocateMemory();
                }
        }

        void wskaznikZDispose()
        {
            for (int i = 0; i < 10; i++)
            {
                    using (WithPointer withPointer = new WithPointer())
                    {
                        withPointer.AllocateMemory();
                    }
            }
        }

        void wskaznikBezDisposeZFinalizerem()
        {
            for (int i = 0; i < 10; i++)
            {
                WithPointerAndFinalizer withPointer = new WithPointerAndFinalizer();
                withPointer.AllocateMemory();
            }
            GC.Collect();
        }
        }
    }

    public class NotDisposed
    {
        readonly string _connectionString;
        SqlConnection _connection;

        public NotDisposed(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string GetDate()
        {
            if (_connection == null)
            {
                _connection = new SqlConnection(_connectionString);
                _connection.Open();
            }
            var command = _connection.CreateCommand();
            command.CommandText = "Select GETDATE()";
            return command.ExecuteScalar().ToString();
        }
    }

    public class Disposed : IDisposable
    {
        readonly string _connectionString;
        SqlConnection _connection;

        public Disposed(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string GetDate()
        {
            if (_connection == null)
            {
                _connection = new SqlConnection(_connectionString);
                _connection.Open();
            }
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "Select GETDATE()";
                return command.ExecuteScalar().ToString();
            }
            return "";
        }

        public void Dispose()
        {
            Console.WriteLine($"Disposed {DateTime.Now.ToLongTimeString()}, connection {_connection.GetHashCode()}");
            _connection.Close();
            _connection.Dispose();
            _connection = null;
        }
    }

    public class DisposedCorrectly : IDisposable
    {
        readonly string _connectionString;
        SqlConnection _connection;

        public DisposedCorrectly(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string GetDate()
        {
            if (_connection == null)
            {
                _connection = new SqlConnection(_connectionString);
                _connection.Open();
            }
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "Select GETDATE()";
                return command.ExecuteScalar().ToString();
            }
            return "";
        }

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Console.WriteLine($"Disposed correctly {DateTime.Now.ToLongTimeString()}, connection {_connection.GetHashCode()}");
                    _connection.Close();
                    _connection.Dispose();
                    _connection = null;
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~DisposedCorrectly()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class WithPointer:IDisposable
    {
        private IntPtr _unmanagedPointer;
        private bool disposedValue;

        public void AllocateMemory()
        {
            if (_unmanagedPointer == IntPtr.Zero)
            {
                _unmanagedPointer = Marshal.AllocHGlobal(100 * 1024 * 1024); //rezerwuje 100MB pamięci
                Thread.Sleep(500);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                if (_unmanagedPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_unmanagedPointer);
                    _unmanagedPointer = IntPtr.Zero;
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class WithPointerAndFinalizer : IDisposable
    {
        private IntPtr _unmanagedPointer;
        private bool disposedValue;

        public void AllocateMemory()
        {
            if (_unmanagedPointer == IntPtr.Zero)
            {
                _unmanagedPointer = Marshal.AllocHGlobal(100 * 1024 * 1024); //rezerwuje 100MB pamięci
                Thread.Sleep(500);
            }
        }

        ~WithPointerAndFinalizer()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                if (_unmanagedPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_unmanagedPointer);
                    _unmanagedPointer = IntPtr.Zero;
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
