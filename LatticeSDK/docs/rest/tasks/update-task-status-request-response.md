## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/tasks/{taskId}/status:
    put:
      operationId: update-task-status
      summary: Update task status
      description: >-
        Updates the status of a Task as it progresses through its lifecycle.


        This method allows agents or operators to report the current state of a
        task,

        which could include changes to task status, and error information.


        Each status update increments the task's status_version. When updating
        status,

        clients must provide the current version to ensure consistency. The
        system rejects

        updates with mismatched versions to prevent race conditions.


        Terminal states (`STATUS_DONE_OK` and `STATUS_DONE_NOT_OK`) are
        permanent; once a task

        reaches these states, no further updates are allowed.
      tags:
        - subpackage_tasks
      parameters:
        - name: taskId
          in: path
          description: ID of task to update status of
          required: true
          schema:
            type: string
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Task status update was successful
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Task'
        '400':
          description: Bad request
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '401':
          description: Unauthorized to access resource
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '404':
          description: The specified resource was not found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TaskStatusUpdate'
servers:
  - url: https://example.developer.anduril.com
