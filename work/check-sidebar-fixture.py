import sqlite3
import sys

connection = sqlite3.connect(sys.argv[1])
total = connection.execute(
    "select sum(has_user_event) from threads"
).fetchone()[0]
top_level = connection.execute(
    "select sum(has_user_event) from threads where source='vscode' or source='cli'"
).fetchone()[0]
subagents = connection.execute(
    "select sum(has_user_event) from threads where source<>'vscode' and source<>'cli'"
).fetchone()[0]
connection.close()

print(f"{total},{top_level},{subagents}")
