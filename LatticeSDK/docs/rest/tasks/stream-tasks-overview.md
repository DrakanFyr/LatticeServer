# Stream tasks

POST https://example.developer.anduril.com/api/v1/tasks/stream
Content-Type: application/json

Establishes a server streaming connection that delivers task updates in real-time using Server-Sent Events (SSE).

The stream delivers all existing non-terminal tasks when first connected, followed by real-time
updates for task creation and status changes. Additionally, heartbeat messages are sent periodically to maintain the connection.

Reference: https://developer.anduril.com/reference/rest/tasks/stream-tasks

