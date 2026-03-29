# Get task

GET https://example.developer.anduril.com/api/v1/tasks/{taskId}

Retrieves a specific Task by its ID, with options to select a particular task version or view.

This method returns detailed information about a task including its current status,
specification, relations, and other metadata. The response includes the complete Task object
with all associated fields.

By default, the method returns the latest definition version of the task from the manager's
perspective.

Reference: https://developer.anduril.com/reference/rest/tasks/get-task

