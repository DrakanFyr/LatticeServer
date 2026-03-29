# Stream as agent

POST https://example.developer.anduril.com/api/v1/agent/stream
Content-Type: application/json

Establishes a server streaming connection that delivers tasks to taskable agents for execution
using Server-Sent Events (SSE).

This method creates a connection from the Tasks API to an agent that streams relevant tasks to the listener agent. The agent receives a stream of tasks that match the entities specified by the tasks' selector criteria.

The stream delivers three types of requests:
- `ExecuteRequest`: Contains a new task for the agent to execute
- `CancelRequest`: Indicates a task should be canceled
- `CompleteRequest`: Indicates a task should be completed

Additionally, heartbeat messages are sent periodically to maintain the connection.

This is recommended method for taskable agents to receive and process tasks in real-time.
Agents should maintain connection to this stream and process incoming tasks according to their capabilities. 

When an agent receives a task, it should update the task status using the `UpdateStatus` endpoint
to provide progress information back to Tasks API.

Reference: https://developer.anduril.com/reference/rest/tasks/stream-as-agent

