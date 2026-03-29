# Create task

POST https://example.developer.anduril.com/api/v1/tasks
Content-Type: application/json

Creates a new Task in the system with the specified parameters.

This method initiates a new task with a unique ID (either provided or auto-generated),
sets the initial task state to STATUS_SENT, and establishes task ownership. The task
can be assigned to a specific agent through the Relations field.

Once created, a task enters the lifecycle workflow and can be tracked, updated, and managed
through other Tasks API endpoints.

Reference: https://developer.anduril.com/reference/rest/tasks/create-task

