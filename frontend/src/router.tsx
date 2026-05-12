import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppLayout } from './layouts/AppLayout';
import { DataSourcesPage } from './pages/assets/DataSourcesPage';
import { SatellitesPage } from './pages/assets/SatellitesPage';
import { SatelliteGroupsPage } from './pages/assets/SatelliteGroupsPage';
import { FilterTemplatesPage } from './pages/templates/FilterTemplatesPage';
import { FilterTemplateEditor } from './pages/templates/FilterTemplateEditor';
import { AlgorithmTemplatesPage } from './pages/templates/AlgorithmTemplatesPage';
import { AlgorithmTemplateEditor } from './pages/templates/AlgorithmTemplateEditor';
import { AlgorithmRegistryPage } from './pages/algorithms/AlgorithmRegistryPage';
import { AlgorithmPackagesPage } from './pages/algorithms/AlgorithmPackagesPage';
import { PipelineTasksPage } from './pages/tasks/PipelineTasksPage';
import { PreprocessTasksPage } from './pages/tasks/PreprocessTasksPage';
import { TasksListPage } from './pages/tasks/TasksListPage';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <AppLayout />,
    children: [
      { index: true, element: <Navigate to="/assets/sources" replace /> },
      { path: 'assets/sources', element: <DataSourcesPage /> },
      { path: 'assets/satellites', element: <SatellitesPage /> },
      { path: 'assets/groups', element: <SatelliteGroupsPage /> },
      { path: 'templates/filters', element: <FilterTemplatesPage /> },
      { path: 'templates/filters/new', element: <FilterTemplateEditor /> },
      { path: 'templates/filters/:templateId/versions/:version', element: <FilterTemplateEditor /> },
      { path: 'templates/algorithms', element: <AlgorithmTemplatesPage /> },
      { path: 'templates/algorithms/new', element: <AlgorithmTemplateEditor /> },
      { path: 'templates/algorithms/:templateId/versions/:version', element: <AlgorithmTemplateEditor /> },
      { path: 'algorithms/registry', element: <AlgorithmRegistryPage /> },
      { path: 'algorithms/packages', element: <AlgorithmPackagesPage /> },
      { path: 'tasks/pipeline', element: <PipelineTasksPage /> },
      { path: 'tasks/preprocess', element: <PreprocessTasksPage /> },
      { path: 'tasks', element: <TasksListPage /> }
    ]
  }
]);
