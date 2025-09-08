import sqlite3
import os

def migrate_database():
    # Path to your database file
    db_path = 'workbot.db'
    
    # Check if database exists
    if not os.path.exists(db_path):
        print(f"Error: Database file '{db_path}' not found!")
        print("Make sure you're running this script from your project directory.")
        return False
    
    try:
        # Connect to the database
        conn = sqlite3.connect(db_path)
        cursor = conn.cursor()
        
        print("Connected to workbot.db successfully!")
        print("Running migration...")
        
        # List of SQL commands to execute
        migration_commands = [
            "ALTER TABLE Users ADD COLUMN DisplayName TEXT NULL;",
            "ALTER TABLE Users ADD COLUMN ExternalId TEXT NULL;",
            "ALTER TABLE Users ADD COLUMN EmployeeId TEXT NULL;",
            "ALTER TABLE Users ADD COLUMN AuthenticationMethod TEXT NOT NULL DEFAULT 'Local';",
            "CREATE INDEX IF NOT EXISTS IX_Users_ExternalId ON Users (ExternalId);",
            "CREATE INDEX IF NOT EXISTS IX_Users_EmployeeId ON Users (EmployeeId);",
            "CREATE INDEX IF NOT EXISTS IX_Users_AuthenticationMethod ON Users (AuthenticationMethod);",
            "UPDATE Users SET AuthenticationMethod = 'Local' WHERE AuthenticationMethod IS NULL OR AuthenticationMethod = '';"
        ]
        
        # Execute each command
        for i, command in enumerate(migration_commands, 1):
            try:
                cursor.execute(command)
                print(f"✓ Step {i}/8: {command.split()[0]} command executed successfully")
            except sqlite3.OperationalError as e:
                if "duplicate column name" in str(e).lower():
                    print(f"⚠ Step {i}/8: Column already exists, skipping: {command}")
                else:
                    print(f"✗ Step {i}/8: Error executing command: {e}")
                    return False
            except Exception as e:
                print(f"✗ Step {i}/8: Unexpected error: {e}")
                return False
        
        # Commit the changes
        conn.commit()
        print("\n✓ All migration steps completed successfully!")
        
        # Verify the changes
        cursor.execute("PRAGMA table_info(Users);")
        columns = cursor.fetchall()
        
        print("\nUpdated Users table structure:")
        for column in columns:
            print(f"  - {column[1]} ({column[2]})")
        
        # Check if we have any users
        cursor.execute("SELECT COUNT(*) FROM Users;")
        user_count = cursor.fetchone()[0]
        print(f"\nTotal users in database: {user_count}")
        
        if user_count > 0:
            cursor.execute("SELECT Username, AuthenticationMethod FROM Users LIMIT 5;")
            sample_users = cursor.fetchall()
            print("\nSample user authentication methods:")
            for user in sample_users:
                print(f"  - {user[0]}: {user[1]}")
        
        return True
        
    except sqlite3.Error as e:
        print(f"Database error: {e}")
        return False
    except Exception as e:
        print(f"Unexpected error: {e}")
        return False
    finally:
        if conn:
            conn.close()
            print("\nDatabase connection closed.")

if __name__ == "__main__":
    print("WorkBot Database Migration Script")
    print("=" * 40)
    print("This script will add Identity Server fields to the Users table.")
    print()
    
    # Get current directory
    current_dir = os.getcwd()
    print(f"Current directory: {current_dir}")
    
    # List .db files in current directory
    db_files = [f for f in os.listdir('.') if f.endswith('.db')]
    if db_files:
        print(f"Found database files: {', '.join(db_files)}")
    else:
        print("No .db files found in current directory.")
    
    print()
    
    # Ask for confirmation
    response = input("Do you want to proceed with the migration? (y/N): ").strip().lower()
    
    if response in ['y', 'yes']:
        success = migrate_database()
        if success:
            print("\n🎉 Migration completed successfully!")
            print("You can now continue with the Identity Server setup.")
        else:
            print("\n❌ Migration failed. Please check the errors above.")
    else:
        print("Migration cancelled.")